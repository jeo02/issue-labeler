using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hubbup.MikLabelModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IssueLabeler
{
    public class AzureSdkIssueLabelerService
    {
        private static readonly ActionResult EmptyResult = new JsonResult(new PredictionResponse(Array.Empty<string>()));
        private static readonly SemaphoreSlim InitSync = new(1, 1);
        private static readonly HashSet<string> CommonModelRepositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool Initialized = false;

        private string CommonModelRepositoryName { get; }
        private ILabelerLite Labeler { get; }
        private IConfiguration Config { get; }
        private IModelHolderFactoryLite ModelHolderFactory { get; }


        public AzureSdkIssueLabelerService(ILabelerLite labeler, IModelHolderFactoryLite modelHolderFactory, IConfiguration config)
        {
            ModelHolderFactory = modelHolderFactory;
            Labeler = labeler;
            Config = config;

            CommonModelRepositoryName = config["CommonModelRepositoryName"];
        }

        [FunctionName("AzureSdkIssueLabelerService")]
        public async Task<ActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "POST", Route = null)] HttpRequest request, ILogger log)
        {
            IssuePayload issue;

            try
            {
                using var bodyReader = new StreamReader(request.Body);

                var requestBody = await bodyReader.ReadToEndAsync();
                issue = JsonConvert.DeserializeObject<IssuePayload>(requestBody);
            }
            catch (Exception ex)
            {
                log.LogError($"Unable to deserialize payload:{ex.Message}{Environment.NewLine}\t{ex}{Environment.NewLine}");
                return new BadRequestResult();
            }

            // If the models haven't been initialized yet, do so now.
            if (!Initialized)
            {
                log.LogInformation("Models have not yet been initialized; loading prediction models.");
                var semaphoreHeld = false;

                try
                {
                    if (!InitSync.Wait(0))
                    {
                        await InitSync.WaitAsync().ConfigureAwait(false);
                    }

                    semaphoreHeld = true;

                    if (!Initialized)
                    {
                        await Initialize(Config, ModelHolderFactory).ConfigureAwait(false);
                        Initialized = true;
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"Error initializing the label prediction models: {ex.Message}{Environment.NewLine}\t{ex}{Environment.NewLine}");
                    return EmptyResult;
                }
                finally
                {
                    if (semaphoreHeld)
                    {
                        InitSync.Release();
                    }

                    log.LogInformation("Model initialization is complete.");
                }
            }

            // Predict labels.
            var predictionRepositoryName = TranslateRepoName(issue.RepositoryName);
            log.LogInformation($"Predicting labels for {issue.RepositoryName} using the `{predictionRepositoryName}` model for issue #{issue.IssueNumber}.");

            try
            {
                // In order for labels to be valid for Azure SDK use, there must
                // be at least two of them, which corresponds to a Service (pink)
                // and Category (yellow).  If that is not met, then no predictions
                // should be returned.
                var predictions = await Labeler.QueryLabelPrediction(
                    issue.IssueNumber,
                    issue.Title,
                    issue.Body,
                    issue.IssueUserLogin,
                    predictionRepositoryName,
                    issue.RepositoryOwnerName);

                if (predictions.Count < 2)
                {
                    log.LogInformation($"No labels were predicted for {issue.RepositoryName} using the `{predictionRepositoryName}` model for issue #{issue.IssueNumber}.");
                    return EmptyResult;
                }

                log.LogInformation($"Labels were predicted for {issue.RepositoryName} using the `{predictionRepositoryName}` model for issue #{issue.IssueNumber}.  Using: [{predictions[0]}, {predictions[1]}].");
                return new JsonResult(new PredictionResponse(predictions));
            }
            catch (Exception ex)
            {
                log.LogError($"Error querying predictions for {issue.RepositoryName} using the `{predictionRepositoryName}` model for issue #{issue.IssueNumber}: {ex.Message}{Environment.NewLine}\t{ex}{Environment.NewLine}");
                return EmptyResult;
            }
        }

        private async Task Initialize(IConfiguration config, IModelHolderFactoryLite modelHolderFactory)
        {
            // Initialize the models to use for prediction.

            var owner = config["RepoOwner"];
            var repos = config["RepoNames"];

            if (repos != null)
            {
                foreach (var repo in repos.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var blobConfig = $"IssueModel:{repo}:BlobConfigNames";

                    if (!string.IsNullOrEmpty(config[blobConfig]))
                    {
                        var blobConfigs = config[blobConfig].Split(';', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var blobConfigName in blobConfigs)
                        {
                            await modelHolderFactory.CreateModelHolder(owner, repo, blobConfigName).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await modelHolderFactory.CreateModelHolder(owner, repo).ConfigureAwait(false);
                    }
                }
            }

            // Initialize the set of repositories that use the common model.

            var commonModelRepos = Config["ReposUsingCommonModel"];

            if (!string.IsNullOrEmpty(commonModelRepos))
            {
                foreach (var repo in commonModelRepos.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    CommonModelRepositories.Add(repo);
                }
            }
        }

        private string TranslateRepoName(string repoName) =>
            CommonModelRepositories.Contains(repoName)
            ? CommonModelRepositoryName
            : repoName;

        // Private type used for deserializing the request payload of issue data.
        private class IssuePayload
        {
            public int IssueNumber;
            public string Title;
            public string Body;
            public string IssueUserLogin;
            public string RepositoryName;
            public string RepositoryOwnerName;
        }

        // Type used for shaping the JSON response payload.
        public record PredictionResponse(IEnumerable<string> labels);
    }
}