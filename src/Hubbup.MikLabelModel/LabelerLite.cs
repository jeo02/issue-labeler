// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using IssueLabeler.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hubbup.MikLabelModel
{
    public class LabelerLite : ILabelerLite
    {
        private Regex _regex = new Regex(@"@[a-zA-Z0-9_//-]+", RegexOptions.Compiled);
        private readonly ILogger<LabelerLite> _logger;
        private readonly IModelHolderFactoryLite _modelHolderFactory;
        private readonly IConfiguration _config;
        private readonly IGitHubClientWrapper _gitHubClientWrapper;
        private const float defaultConfidenceThreshold = 0.60f;
        private const string defaultModel = "default model";

        public LabelerLite(
            ILogger<LabelerLite> logger,
            IGitHubClientWrapper gitHubClientWrapper,
            IModelHolderFactoryLite modelHolderFactory,
            IConfiguration config)
        {
            _gitHubClientWrapper = gitHubClientWrapper;
            _logger = logger;
            _modelHolderFactory = modelHolderFactory;
            _config = config;
        }

        public async Task ApplyLabelPrediction(string owner, string repo, int number, Func<LabelSuggestion, Issue, float, bool> shouldApplyLabel)
        {
            _logger.LogInformation($"ApplyLabelPrediction started query for {owner}/{repo}#{number}");
            int updateAttempt = 0;
            while (updateAttempt < 3)
            {
                updateAttempt++;
                var iop = await _gitHubClientWrapper.GetIssue(owner, repo, number);
                string body = iop.Body ?? string.Empty;
                var userMentions = _regex.Matches(body).Select(x => x.Value).ToArray();
                var issueModel = CreateIssue(iop.Number, iop.Title, iop.Body, userMentions, iop.User.Login);
                var issueUpdate = iop.ToUpdate();
                _config.TryGetConfigValue($"IssueModel:{repo}:WhatIf", out var whatIf, "true");

                // Get predictions
                var configuredConfidence = _config[$"IssueModel:{repo}:BlobName:ConfidenceThreshold"];
                float confidence = defaultConfidenceThreshold;
                if (!float.TryParse(configuredConfidence, out confidence))
                {
                    confidence = defaultConfidenceThreshold;
                    _logger.LogInformation($"Prediction confidence default threshold of {confidence} will be used as no value was configured. {owner}/{repo}#{number}");
                }
                else
                {
                    _logger.LogInformation($"Prediction confidence threshold of {confidence} will be used. {owner}/{repo}#{number}");
                }

                var predictions = await GetPredictions(owner, repo, number, issueModel);
                bool issueMissingLabels = false;
                bool predictionBelowConfidenceThreshold = false;
                StringBuilder commentText = new StringBuilder();
                foreach (var labelSuggestion in predictions)
                {
                    if (!shouldApplyLabel(labelSuggestion, iop, confidence))
                    {
                        _logger.LogWarning($"Failed: shouldApplyLabel func returned false for {owner}/{repo}#{number} Model:{labelSuggestion.ModelConfigName ?? defaultModel}");
                        commentText.AppendLine($"Label prediction was below confidence level `{confidence}` for Model:`{labelSuggestion.ModelConfigName ?? defaultModel}`: '{string.Join(",", labelSuggestion.LabelScores.Select(x => $"{x.LabelName}:{x.Score}"))}'");
                        predictionBelowConfidenceThreshold = true;
                        continue;
                    }

                    var topChoice = labelSuggestion.LabelScores.OrderByDescending(x => x.Score).First();
                    if (!iop.Labels.Any(l => l.Name == topChoice.LabelName))
                    {
                        issueMissingLabels = true;
                        // Add the label to the local issueUpdate model.
                        issueUpdate.AddLabel(topChoice.LabelName);
                    }
                    else
                    {
                        _logger.LogInformation($"Success(partial): Issue {owner}/{repo}#{number} already tagged with label '{topChoice.LabelName}'");
                    }
                }

                var success = await UpdateIssueAsync(owner, repo, number, issueUpdate, iop.UpdatedAt, commentText.ToString(), issueMissingLabels, predictionBelowConfidenceThreshold, whatIf);
                if (success)
                {
                    return;
                }
            }
            _logger.LogError($"Failed: Tried to update Issue {owner}/{repo}#{number} {updateAttempt} times.");
        }

        private async Task<List<LabelSuggestion>> GetPredictions(string owner, string repo, int number, GitHubIssue issueModel)
        {
            List<LabelSuggestion> predictions = new List<LabelSuggestion>();
            List<IPredictor> predictors = new List<IPredictor>();

            if (_config.TryGetConfigValue($"IssueModel:{repo}:BlobConfigNames", out var blobConfig))
            {
                var blobConfigs = blobConfig.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var blobConfigName in blobConfigs)
                {
                    // get a prediction for each model
                    var predictor = await _modelHolderFactory.GetPredictor(owner, repo, blobConfigName);
                    predictors.Add(predictor);
                }
            }
            else
            {
                // Add just the default predictor
                var predictor = await _modelHolderFactory.GetPredictor(owner, repo);
                predictors.Add(predictor);
            }

            foreach (var predictor in predictors)
            {
                var labelSuggestion = await predictor.Predict(issueModel);
                labelSuggestion.ModelConfigName = predictor.ModelName;
                if (labelSuggestion == null)
                {
                    _logger.LogCritical($"Failed: Unable to get prediction for {owner}/{repo}#{number}. ModelName:{predictor.ModelName}");
                    return null;
                }
                _logger.LogInformation($"Prediction results for {owner}/{repo}#{number}, Model:{labelSuggestion.ModelConfigName ?? defaultModel}: '{string.Join(",", labelSuggestion.LabelScores.Select(x => $"{x.LabelName}:{x.Score}"))}'");
                predictions.Add(labelSuggestion);
            }

            return predictions;
        }

        private async Task<bool> UpdateIssueAsync(string owner, string repo, int number, IssueUpdate issueUpdate, DateTimeOffset? lastUpdated, string commentText, bool issueMissingLabels, bool predictionBelowConfidenceThreshold, string whatIf)
        {
            if (whatIf != "false")
            {
                _logger.LogInformation($"Whatif=true. Issue {owner}/{repo}#{number} would have been updated.");
                return true;
            }

            // If the prediction was below the confidence level, comment with the prediction results
            if (predictionBelowConfidenceThreshold)
            {
                _config.TryGetConfigValue($"IssueModel:{repo}:CommentOnFailure", out var commentOnFailure, "false");
                if (commentOnFailure == "true" && whatIf == "false")
                {
                    await _gitHubClientWrapper.CommentOn(owner, repo, number, commentText);
                }
                return true;
            }

            // If the issue was already labeled with our predictions, no update is needed
            if (!issueMissingLabels)
            {
                _logger.LogInformation($"Success: Did not update Issue {owner}/{repo}#{number} because it already has the predicted labels");
                return true;
            }

            // If configured, add a success label
            if (_config.TryGetConfigValue($"IssueModel:{repo}:SuccessLabel", out var successLabel))
            {
                issueUpdate.AddLabel(successLabel);
            }

            // Check that the issue hasn't updated since we initially got the message.
            var iop = await _gitHubClientWrapper.GetIssue(owner, repo, number);
            if (iop.UpdatedAt != lastUpdated)
            {
                _logger.LogWarning($"Issue {owner}/{repo}#{number} was updated since we fetched it, retrying.");
                return false;
            }

            _logger.LogInformation($"Issue Lables for {owner}/{repo}#{number} prior to update: '{string.Join(',', iop.Labels.Select(l => l.Name))}'");

            // Update the issue
            await _gitHubClientWrapper.UpdateIssue(owner, repo, number, issueUpdate);
            _logger.LogInformation($"Success: Updated Issue {owner}/{repo}#{number}");
            return true;
        }

        private static GitHubIssue CreateIssue(int number, string title, string body, string[] userMentions, string author)
        {
            return new GitHubIssue()
            {
                ID = number,
                Title = title,
                Description = body,
                IsPR = 0,
                Author = author,
                UserMentions = string.Join(' ', userMentions),
                NumMentions = userMentions.Length
            };
        }
    }
}
