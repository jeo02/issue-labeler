// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using IssueLabeler.Shared;
using IssueLabeler.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;
using System;
using System.Linq;
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



        public async Task ApplyLabelPrediction(string owner, string repo, int number, Func<LabelSuggestion, Issue, bool> shouldApplyLabel)
        {
            _logger.LogInformation($"ApplyLabelPrediction started query for {owner}/{repo}#{number}");

            var modelHolder = await _modelHolderFactory.CreateModelHolder(owner, repo);
            if (modelHolder == null)
            {
                _logger.LogError($"Repo {owner}/{repo} is not yet configured for label prediction.");
                return;
            }
            if (!modelHolder.IsIssueEngineLoaded)
            {
                _logger.LogError("load engine before calling predict");
                return;
            }

            // Load issue
            var iop = await _gitHubClientWrapper.GetIssue(owner, repo, number);
            string body = iop.Body ?? string.Empty;
            var userMentions = _regex.Matches(body).Select(x => x.Value).ToArray();
            var issueModel = CreateIssue(iop.Number, iop.Title, iop.Body, userMentions, iop.User.Login);

            // Do prediction
            var labelSuggestion = await Predictor.Predict(issueModel, _logger, modelHolder);
            if (labelSuggestion == null)
            {
                _logger.LogCritical($"Failed: Unable to get prediction for {owner}/{repo}#{number}");
                return;
            }
            _logger.LogInformation($"Prediction results for {owner}/{repo}#{number}: '{string.Join(",", labelSuggestion.LabelScores.Select(x => $"{x.LabelName}:{x.Score}"))}'");


            if (!shouldApplyLabel(labelSuggestion, iop))
            {
                _logger.LogWarning($"Failed: shouldApplyLabel func returned false for {owner}/{repo}#{number}");
                return;
            }

            var topChoice = labelSuggestion.LabelScores.OrderByDescending(x => x.Score).First();
            if (iop.Labels.Any(l => l.Name == topChoice.LabelName))
            {
                _logger.LogInformation($"Success: Issue {owner}/{repo}#{number} already tagged with label '{topChoice.LabelName}'");
                return;
            }

            var configSection = $"IssueModel:{repo}:WhatIf";
            var whatIf = _config[configSection];
            if (string.IsNullOrEmpty(whatIf) || whatIf != "false")
            {
                _logger.LogInformation($"Whatif=true. Issue {owner}/{repo}#{number} would have been tagged with label '{topChoice.LabelName}'");
                return;
            }

            // Update issue
            var issueUpdate = iop.ToUpdate();
            issueUpdate.AddLabel(topChoice.LabelName);
            await _gitHubClientWrapper.UpdateIssue(owner, repo, number, issueUpdate);
            _logger.LogInformation($"Success: Issue {owner}/{repo}#{number} tagged with label '{topChoice.LabelName}'");
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
