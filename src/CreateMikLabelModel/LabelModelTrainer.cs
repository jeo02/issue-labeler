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
using CreateMikLabelModel.DL.GraphQL;
using CreateMikLabelModel.ML;
using CreateMikLabelModel.Models;

namespace CreateMikLabelModel
{
    public class LabelModelTrainer
    {
        public string RepositoryPath { get; init; }

        public LabelModelTrainer(string repositoryPath) => RepositoryPath = repositoryPath;

        public Task PrepareTrainingSet(string gitHubAccessToken, string trainingDataBasePath, Func<object, bool> labelFilter = default) =>
            PrepareTrainingSet(gitHubAccessToken, trainingDataBasePath, new[] { RepositoryPath }, labelFilter);

        public async Task PrepareTrainingSet(string gitHubAccessToken, string trainingDataBasePath, string[] trainingRepositoryGroup, Func<object, bool> labelFilter = default)
        {
            if (string.IsNullOrEmpty(gitHubAccessToken))
            {
                throw new ArgumentException("GitHub access token is required.", nameof(gitHubAccessToken));
            }

            if (string.IsNullOrEmpty(trainingDataBasePath))
            {
                throw new ArgumentException("The base path for storing training data is required.", nameof(gitHubAccessToken));
            }

            if (!Directory.Exists(trainingDataBasePath) || !ValidateWriteAccess(trainingDataBasePath))
            {
                throw new ArgumentException("Either the directory does not exist or cannot be written to.", nameof(trainingDataBasePath));
            }

            if (trainingRepositoryGroup == null || trainingRepositoryGroup.Length == 0)
            {
                throw new ArgumentException("The repository group is required and should contain at least one item.", nameof(trainingRepositoryGroup));
            }

            // If no explicit filter was requested, accept all labels as valid for the training set.

            labelFilter ??= AllLabelsFilter;

            // Parse the repository information and prepare the information needed for training set preparation.

            var repositoryInformation = RepositoryInformation.Parse(RepositoryPath);
            var trainingFilePrevfix = $"{ repositoryInformation.Owner }-{ repositoryInformation.Name }";
            var issueFiles = new DataFilePaths(trainingDataBasePath, trainingFilePrevfix, forPrs: false, skip: false);
            var pullRequestFiles = new DataFilePaths(trainingDataBasePath, trainingFilePrevfix, forPrs: true, skip: false);

            // Download the raw GitHub data into a single tab-delimited set.

            await DownloadTrainingItemsAsync(issueFiles.InputPath, trainingRepositoryGroup.Select(item => RepositoryInformation.Parse(item)), gitHubAccessToken, labelFilter).ConfigureAwait(false);
        }

        private static async Task DownloadTrainingItemsAsync(string outputPath, IEnumerable<RepositoryInformation> repositories, string gitHubAccessToken, Func<object, bool> labelFilter, bool preferComplehensiveDownload = true)
        {
            var stopWatch = Stopwatch.StartNew();
            var outputLinesExcludingHeader = new Dictionary<TrainingItem, string>();

            using var outputWriter = new StreamWriter(outputPath);
            CommonHelper.WriteCsvHeader(outputWriter);

            var completed = preferComplehensiveDownload switch
            {
                true => await OctokitGraphQLClient.DownloadIssuesAndPullRequests(repositories, gitHubAccessToken, outputLinesExcludingHeader, outputWriter, labelFilter).ConfigureAwait(false),
                false => throw new NotImplementedException("Fast and loose download isn't implemented yet.") // This is where GraphQLDownloadHelper was used.
            };

            if (!completed)
            {
                throw new ApplicationException("The data needed for training was unable to be fully downloaded.");
            }

            stopWatch.Stop();
            Trace.WriteLine($"Done downloading data for training items in {stopWatch.Elapsed.TotalSeconds:0.00} seconds.");
        }

        private static bool ValidateWriteAccess(string path)
        {
            try
            {
                using var file = File.Create(Path.Combine(path, Path.GetRandomFileName()), 1, FileOptions.DeleteOnClose);
                file.Close();

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool AllLabelsFilter (object label) => true;
    }
}
