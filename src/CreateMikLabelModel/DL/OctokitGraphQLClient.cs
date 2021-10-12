// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CreateMikLabelModel.DL.Common;
using CreateMikLabelModel.Models;
using Octokit.GraphQL;

using GraphQLConnection = Octokit.GraphQL.Connection;
using GraphQLProductHeaderValue = Octokit.GraphQL.ProductHeaderValue;

namespace CreateMikLabelModel.DL.GraphQL
{

    public static class OctokitGraphQLClient
    {
        private static readonly bool[] FlagValues = new[] { false, true };
        private static GraphQLConnection _connection;

        public static async Task<bool> DownloadIssuesAndPullRequests(
            IEnumerable<RepositoryInformation> repositories,
            string githubAccessToken,
            Dictionary<TrainingItem, string> outputLines,
            StreamWriter outputWriter,
            Func<object, bool> labelFilter)
        {
            var missingIssues = 0;
            var missingPullRequests = 0;

            try
            {
                _connection = GetOrCreateConnection(githubAccessToken);

                foreach (var repository in repositories)
                {
                    var labelsWithCounts = await GetFilteredLabelsAndCounts(_connection, repository.Owner, repository.Name, labelFilter).ConfigureAwait(false);
                    var labels = labelsWithCounts.Keys.ToList();
                    var countForPullRequests = labelsWithCounts.ToDictionary(x => x.Key, x => x.Value.totalPrsCount);
                    var countForIssues = labelsWithCounts.ToDictionary(x => x.Key, x => x.Value.totalIssuesCount);

                    foreach (var isPullRequest in FlagValues)
                    {
                        foreach (var label in labels)
                        {
                            var items = isPullRequest ? "Pull Requests" : "Issues";
                            var missing = 0;

                            Trace.WriteLine($"Downloading {items} for '{label}'.");

                            var records = isPullRequest switch
                            {
                                true => await GetPullRequestsForLabel(_connection, label, repository.Owner, repository.Name, countForPullRequests).ConfigureAwait(false),
                                false => await GetIssuesForLabel(_connection, label, repository.Owner, repository.Name, countForIssues).ConfigureAwait(false)
                            };

                            Trace.WriteLine($"Downloaded {records.Count} {items} with '{label}' label.");
                            outputLines.AddRange(records);

                            if (isPullRequest)
                            {
                                missing = countForPullRequests[label] - records.Count;
                                missingPullRequests += missing;
                            }
                            else
                            {
                                missing = countForIssues[label] - records.Count;
                                missingIssues += missing;
                            }

                            if (missing > 0)
                            {
                                Trace.WriteLine($"Possibly missing {missing} {items} to download later.");
                            }
                        }
                    }
                }

                // Attempt to reconcile missing items.

                if ((missingIssues > 0) || (missingPullRequests > 0))
                {
                    foreach (var repository in repositories)
                    {
                        var missingItems = await OctokitGitHubClient.DownloadMissingIssueAndPullRequestsAsync(repository, githubAccessToken, outputLines, missingIssues, missingPullRequests, labelFilter).ConfigureAwait(false);
                        outputLines.AddRange(missingItems);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{nameof(DownloadIssuesAndPullRequests)}: {ex.Message}");
                return false;
            }
            finally
            {
               CommonHelper.saveAction(outputLines, outputWriter);
            }
        }

        private static GraphQLConnection GetOrCreateConnection(string accessToken) =>
            _connection ??= new GraphQLConnection(new GraphQLProductHeaderValue("GitHub.Issue.Labeler", "1.0"), GraphQLConnection.GithubApiUri, accessToken);

        private static async Task<Dictionary<TrainingItem, string>> GetPullRequestsForLabel(
            GraphQLConnection connection,
            string label,
            string owner,
            string repository,
            Dictionary<string, int> countPerLabel)
        {
            var query = new Query()
                .Repository(owner: Variable.Var("owner"), name: Variable.Var("name"))
                .PullRequests(first: 100, after: Variable.Var("after"), null, null, null, null, new[] { label })
                .Select(connection => new
                {
                    connection.PageInfo.EndCursor,
                    connection.PageInfo.HasNextPage,
                    connection.TotalCount,
                    Items = connection.Nodes.Select(issue => new
                    {
                        Number = issue.Number,
                        Files = issue.Files(null, null, null, null).AllPages().Select(x => x.Path).ToList(),
                        AuthorLogin = issue.Author.Login,
                        Body = issue.Body,
                        Title = issue.Title,
                        CreatedAt = issue.CreatedAt
                    }).ToList(),
                }).Compile();

            try
            {
                // For the first page, set `after` to null.
                var vars = new Dictionary<string, object>
                {
                    { "owner", owner },
                    { "name", repository },
                    { "label", label },
                    { "after", null },
                };

                // Read the first page.
                var result = await connection.Run(query, vars).ConfigureAwait(false);
                var timesRetried = 0;

                while (result == null && timesRetried < 5)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    result = await connection.Run(query, vars);
                    timesRetried++;
                }

                if (result == null)
                {
                    Trace.WriteLine($"Skipping pull requests for {owner}/{repository} with label '{label}' and moving on.");
                    Trace.WriteLine($"Expected {countPerLabel[label]} pull requests with label '{label}' but found none.");
                    return new Dictionary<TrainingItem, string>();
                }

                // If there are more pages, set `after` to the end cursor.
                vars["after"] = result.HasNextPage ? result.EndCursor : null;

                try
                {
                    while (vars["after"] != null)
                    {
                        // Read the next page.
                        var page = await connection.Run(query, vars).ConfigureAwait(false);

                        timesRetried = 0;
                        while (page == null && timesRetried < 5)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5));
                            page = await connection.Run(query, vars);
                            timesRetried++;
                        }
                        if (page != null)
                        {
                            // Add the results from the page to the result.
                            result.Items.AddRange(page.Items);

                            // If there are more pages, set `after` to the end cursor.
                            vars["after"] = page.HasNextPage ? page.EndCursor : null;
                        }
                        else
                        {
                            Trace.WriteLine($"Failed to get some pull requests for {owner}/{repository} with label '{label}'; moving on.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.Message);
                    Trace.WriteLine($"Failed to get all pull requests for {owner}/{repository} with label '{label}'.");
                    Trace.WriteLine($"Taking {result.Items.Count} of {result.TotalCount} pull requests for {owner}/{repository} with label '{label}'; moving on.");
                }

                return result.Items
                    .ToDictionary(x => new TrainingItem(x.CreatedAt, x.Number, repository), x => CommonHelper.GetCompressedLine(
                        x.Files,
                        label,
                        x.AuthorLogin,
                        x.Body,
                        x.Title,
                        x.CreatedAt,
                        x.Number,
                        repository,
                        isPullRequest: true));
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
                Trace.WriteLine($"Failed to get any pull requests for {owner}/{repository} with label '{label}'; moving on.");
                return new Dictionary<TrainingItem, string>();
            }
            finally
            {
                Trace.WriteLine($"Note: Expected {countPerLabel[label]} pull requests with label '{label}'.");
            }
        }

        private static async Task<Dictionary<TrainingItem, string>> GetIssuesForLabel(
            GraphQLConnection connection,
            string label,
            string owner,
            string repository,
            Dictionary<string, int> countPerLabel)
        {
            var query = new Query()
                .Repository(owner: Variable.Var("owner"), name: Variable.Var("name"))
                .Issues(first: Variable.Var("first"), after: Variable.Var("after"), null, null, null, new[] { label }, null, null)
                .Select(connection => new
                {
                    connection.PageInfo.EndCursor,
                    connection.PageInfo.HasNextPage,
                    connection.TotalCount,
                    Items = connection.Nodes.Select(issue => new
                    {
                        Number = issue.Number,
                        AuthorLogin = issue.Author.Login,
                        Body = issue.Body,
                        Title = issue.Title,
                        CreatedAt = issue.CreatedAt
                    }).ToList(),
                }).Compile();

            // For the first page, set `after` to null.
            var vars = new Dictionary<string, object>
            {
                { "owner", owner },
                { "name", repository },
                { "label", label },
                { "after", null },
                { "first", 100 },
            };

            try
            {
                // Read the first page.
                var result = await connection.Run(query, vars);
                var timesRetried = 0;

                while (result == null && timesRetried < 5)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    result = await connection.Run(query, vars);
                    timesRetried++;
                }

                if (result == null)
                {
                    Trace.WriteLine($"Skipping issues for {owner}/{repository} with label '{label}' and moving on.");
                    Trace.WriteLine($"Expected {countPerLabel[label]} pull requests with label `{label}` but found none.");
                    return new Dictionary<TrainingItem, string>();
                }

                // If there are more pages, set `after` to the end cursor.
                vars["after"] = result.HasNextPage ? result.EndCursor : null;

                try
                {
                    while (vars["after"] != null)
                    {
                        // Read the next page.
                        var page = await connection.Run(query, vars);

                        timesRetried = 0;
                        while (page == null && timesRetried < 5)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5));
                            page = await connection.Run(query, vars);
                            timesRetried++;
                        }
                        if (page != null)
                        {
                            // Add the results from the page to the result.
                            result.Items.AddRange(page.Items);

                            // If there are more pages, set `after` to the end cursor.
                            vars["after"] = page.HasNextPage ? page.EndCursor : null;
                        }
                        else
                        {
                            Trace.WriteLine($"Failed to get some issues for {owner}/{repository} with label '{label}'; moving on.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.Message);
                    Trace.WriteLine($"Failed to get all issues for {owner}/{repository} with label '{label}'.");
                    Trace.WriteLine($"Taking {result.Items.Count} of {result.TotalCount} issues for {owner}/{repository} with label '{label}'; moving on.");
                }
                return result.Items
                    .ToDictionary(x => new TrainingItem(x.CreatedAt, x.Number, repository), x => CommonHelper.GetCompressedLine(
                        null,
                        label,
                        x.AuthorLogin,
                        x.Body,
                        x.Title,
                        x.CreatedAt,
                        x.Number,
                        repository,
                        isPullRequest: false));
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
                Trace.WriteLine($"Failed to get any issues for {owner}/{repository} with label '{label}'; moving on.");
                return new Dictionary<TrainingItem, string>();
            }
            finally
            {
                Trace.WriteLine($"Note: Expected {countPerLabel[label]} issues with label '{label}'.");
            }
        }

        private static async Task<Dictionary<string, (int totalIssuesCount, int totalPrsCount)>> GetFilteredLabelsAndCounts(
            GraphQLConnection connection,
            string owner,
            string repository,
            Func<object, bool> labelFilter)
        {
            var query = new Query()
                .Repository(repository, owner)
                .Select(repository => new
                {
                    Name = repository.Name,
                    Labels = repository.Labels(null, null, null, null, null, null).AllPages().Select(label => new
                    {
                        Name = label.Name,
                        Color = label.Color,
                        TotalPrCount = label.PullRequests(null, null, null, null, null, null, null, null, null).TotalCount,
                        TotalIssueCount = label.Issues(null, null, null, null, null, null, null, null).TotalCount,
                    }).ToDictionary(x => x.Name, x => x)
                }).Compile();

            var result = await connection.Run(query).ConfigureAwait(false);

            return result.Labels
                .Where(label => labelFilter(label.Value))
                .ToDictionary(x => x.Key, x => ((int)x.Value.TotalIssueCount, (int)x.Value.TotalPrCount));
        }
    }
}
