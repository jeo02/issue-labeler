using IssueLabeler.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Octokit;
using System;
using System.Collections.Generic;

namespace Hubbup.MikLabelModel.Tests
{
    public class LabelerLiteTests
    {
        Mock<IConfiguration> mockConfig = new Mock<IConfiguration>();
        Mock<ILogger<LabelerLite>> mockLogger = new Mock<ILogger<LabelerLite>>();
        Mock<IModelHolderFactoryLite> mockModelFactory = new Mock<IModelHolderFactoryLite>();
        Mock<IGitHubClientWrapper> mockGitHubClient = new Mock<IGitHubClientWrapper>();
        Mock<IPredictor> mockPredictor = new Mock<IPredictor>();
        ILabelerLite target = null;
        Issue issue = new Issue("", "", "", "", number, ItemState.Open, "", "", new User(), new User(), new List<Octokit.Label>(), new User(), null, null, 0, null, null, DateTimeOffset.Now, null, 1234, "", false, new Octokit.Repository(1234), null);

        private const string owner = "someOwner";
        private const string repo = "someRepo";
        private const int number = 1234;
        private LabelSuggestion suggestion = new LabelSuggestion { LabelScores = new List<ScoredLabel> { new ScoredLabel { LabelName = "someLabel", Score = 0 } } };

        [SetUp]
        public void Setup()
        {
            mockGitHubClient.Reset();
            mockGitHubClient.Setup(m => m.GetIssue(owner, repo, number)).ReturnsAsync(issue);
            mockModelFactory.Setup(m => m.GetPredictor(owner, repo, It.IsAny<String>())).ReturnsAsync(mockPredictor.Object);
            mockPredictor.Setup(m => m.Predict(It.IsAny<GitHubIssue>())).ReturnsAsync(suggestion);
            mockConfig.Setup(m => m[$"IssueModel:{repo}:WhatIf"]).Returns("false");
            target = new LabelerLite(mockLogger.Object, mockGitHubClient.Object, mockModelFactory.Object, mockConfig.Object);
        }

        [Test]
        public void LabelAppliedBasedOnResultOfShouldApplyLabelReturns([Values(true, false)] bool shouldApply)
        {
            target.ApplyLabelPrediction(owner, repo, number, (_, _, _) => shouldApply);

            mockGitHubClient.Verify(m => m.UpdateIssue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IssueUpdate>()),
                Times.Exactly(shouldApply ? 1 : 0));
        }
    }
}