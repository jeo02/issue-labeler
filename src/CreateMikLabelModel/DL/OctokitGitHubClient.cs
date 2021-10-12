// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CreateMikLabelModel.DL.Common;
using CreateMikLabelModel.Models;
using Octokit;

namespace CreateMikLabelModel.DL
{
    public static class OctokitGitHubClient
    {
        private static GitHubClient _client;

        public static async Task<Dictionary<TrainingItem, string>> DownloadMissingIssueAndPullRequestsAsync(
            RepositoryInformation repository,
            string githubAccessToken,
            Dictionary<TrainingItem, string> lookup,
            int missingIssueCount,
            int missingPullRequestCount,
            Func<object, bool> labelFilter)
        {
            _client = GetOrCreateClient(githubAccessToken);

            var remainingItems = new Dictionary<TrainingItem, string>();
            var filteredItems = await GetFilteredItems(_client, repository, labelFilter).ConfigureAwait(false);

            var labeledItemNumbers = filteredItems
                .Where(x => x.RepositoryName.Equals(repository.Name, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Identifier)
                .ToHashSet();

            var orderedNonTransferred = lookup
                .Where(x => x.Key.RepositoryName.Equals(repository.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Key.CreatedAt.UtcDateTime.ToFileTimeUtc())  //-> first by created date
                .ThenBy(x => x.Key.RepositoryName)                          //-> then by repository name
                .ThenBy(x => x.Key.Identifier);                             //-> then by issue number

            if (orderedNonTransferred.Any())
            {
                var lastIssueNumber = orderedNonTransferred.Last().Key.Identifier;

                var existingNonTransferredLookup = orderedNonTransferred
                    .Where(x => x.Key.RepositoryName.Equals(repository.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Key.Identifier)
                    .ToHashSet();

                var missingItemNumbers = labeledItemNumbers
                    .Where(x => !existingNonTransferredLookup.Contains(x))
                    .ToHashSet();

                var itemsToSkip = orderedNonTransferred
                    .Select(x => x.Key.Identifier)
                    .ToHashSet();

                Trace.WriteLine($"There were {missingIssueCount} missing issues and {missingPullRequestCount} missing pull requests identified as missing.");
                Trace.WriteLine($"Those were filtered down to {missingItemNumbers.Count} items that have a single label of interest on them. (items with multiple label of interest are ignored for training).");
                Trace.WriteLine($"Downloading issues and pull requests [{string.Join(", ", missingItemNumbers)}.");

                remainingItems = await DownloadMissingItemsAsync(
                    _client,
                    repository,
                    itemsToSkip,
                    labeledItemNumbers,
                    missingItemNumbers.ToList(),
                    labelFilter).ConfigureAwait(false);

                Trace.WriteLine($"Downloaded {remainingItems.Count} more items for training.");
            }

            return remainingItems;
        }

        private static GitHubClient GetOrCreateClient(string accessToken) =>
            _client ??= new GitHubClient(new ProductHeaderValue("GitHub.Issue.Labeler")) { Credentials = new(accessToken) };

        private static async Task<HashSet<TrainingItem>> GetFilteredItems(
            GitHubClient client,
            RepositoryInformation repository,
            Func<object, bool> labelFilter)
        {
            var request = new RepositoryIssueRequest()
            {
                State = ItemStateFilter.All
            };

            var issues = await client.Issue.GetAllForRepository(repository.Owner, repository.Name, request).ConfigureAwait(false);
            var labeledIssues = issues.Where(issue => issue.Labels.Any(x => labelFilter(x)));
            var unexpectedIssue = labeledIssues.FirstOrDefault(x => !x.HtmlUrl.Contains(repository.Name, StringComparison.OrdinalIgnoreCase));

            if (unexpectedIssue != null)
            {
                Trace.WriteLine($"There was an unexpected result.  Please investigate #{unexpectedIssue.Number} at: '{unexpectedIssue.HtmlUrl}'");
            }

            return labeledIssues.Select(x => new TrainingItem(x.CreatedAt, x.Number, repository.Name)).ToHashSet();
        }

        private static async Task<Dictionary<TrainingItem, string>> DownloadMissingItemsAsync(
            GitHubClient client,
            RepositoryInformation repository,
            HashSet<int> issuesToSkip,
            HashSet<int> itemsWithLabelsOfInterest,
            IList<int> missingItems,
            Func<object, bool> labelFilter)
        {
            var reportProcessCount = 100;
            var counter = 0;

            var itemsToTrack = new Dictionary<TrainingItem, string>();

            for (var index = 0; index < missingItems.Count; ++index)
            {
                var missingItemNumber = missingItems[index];

                if (issuesToSkip.Contains(missingItemNumber) || !itemsWithLabelsOfInterest.Contains(missingItemNumber))
                {
                    continue;
                }

                if (counter++ % reportProcessCount == 0)
                {
                    Trace.WriteLine($"Downloading more missing items... now at #{counter}.");
                }

                Issue currentItem;
                List<string> filePaths;
                Label[] labelsOfInterest;
                bool isPullRequest;

                try
                {
                    currentItem = await client.Issue.Get(repository.Owner, repository.Name, missingItemNumber).ConfigureAwait(false);
                    labelsOfInterest = currentItem.Labels.Where(x => labelFilter(x)).ToArray();

                    // If there were no labels of interest, or there were more than one, ignore the item.

                    if (labelsOfInterest.Length > 0)
                    {
                        continue;
                    }

                    isPullRequest = currentItem.PullRequest != null;

                    if (isPullRequest)
                    {
                        var prFiles = await client.PullRequest.Files(repository.Owner, repository.Name, index).ConfigureAwait(false);
                        filePaths = prFiles.Select(x => x.FileName).ToList();
                    }
                    else
                    {
                        filePaths = null;
                    }
                }
                catch (NotFoundException)
                {
                    Trace.WriteLine($"Issue #{index} not found. Will skip and continue to next.");
                    continue;
                }
                catch (RateLimitExceededException ex)
                {
                    var timeToWait = ex.Reset.AddMinutes(1) - DateTimeOffset.UtcNow;

                    Trace.WriteLine($"The Rate limit exceeded while downloading {index}. Error: '{ex.Message}'");
                    Trace.WriteLine($"Throttling requests to reset the limit... please wait!");

                    index--;
                    await Task.Delay((int)timeToWait.TotalMilliseconds).ConfigureAwait(false);
                    continue;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"downloading #{index} threw (Will skip and continue to next): {ex.Message}");
                    continue;
                }

                // Because each item may have more than one label of interest, a separate
                // entry should be created for each label.

                foreach (var label in labelsOfInterest)
                {
                    itemsToTrack.Add(new TrainingItem(currentItem.CreatedAt, currentItem.Number, repository.Name), CommonHelper.GetCompressedLine(
                        filePaths,
                        label.Name,
                        currentItem.User.Login,
                        currentItem.Body,
                        currentItem.Title,
                        currentItem.CreatedAt,
                        currentItem.Number,
                        repository.Name,
                        isPullRequest));
                }
            }

            return itemsToTrack;
        }

    }
}
