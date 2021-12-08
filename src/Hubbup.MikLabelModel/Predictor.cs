// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using IssueLabeler.Shared;
using IssueLabeler.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Linq;

namespace Hubbup.MikLabelModel
{
    public static class Predictor
    {
        public static LabelSuggestion Predict(IssueModel issue, ILogger logger, IModelHolder modelHolder)
        {
            return Predict(issue, modelHolder.IssuePredEngine, logger);
        }

        public static LabelSuggestion Predict(PrModel issue, ILogger logger, IModelHolder modelHolder)
        {
            if (modelHolder.UseIssuesForPrsToo)
            {
                return Predict(issue, modelHolder.IssuePredEngine, logger);
            }
            return Predict(issue, modelHolder.PrPredEngine, logger);
        }

        private static LabelSuggestion Predict<T>(
            T issueOrPr,
            PredictionEngine<T, GitHubIssuePrediction> predEngine,
            ILogger logger)
            where T : IssueModel
        {
            if (predEngine == null)
            {
                throw new InvalidOperationException("expected prediction engine loaded.");
            }

            GitHubIssuePrediction prediction = predEngine.Predict(issueOrPr);

            VBuffer<ReadOnlyMemory<char>> slotNames = default;
            predEngine.OutputSchema[nameof(GitHubIssuePrediction.Score)].GetSlotNames(ref slotNames);

            float[] probabilities = prediction.Score;
            var labelPredictions = MikLabelerPredictor.GetBestThreePredictions(probabilities, slotNames);

            float maxProbability = probabilities.Max();
            logger.LogInformation($"# {maxProbability} {prediction.Area} for #{issueOrPr.Number} {issueOrPr.Title}");
            return new LabelSuggestion
            {
                LabelScores = labelPredictions,
            };
        }
    }
}
