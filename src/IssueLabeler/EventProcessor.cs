using Hubbup.MikLabelModel;
using IssueLabeler.Shared;
using Microsoft.Extensions.Logging;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IssueLabeler
{
    internal class EventProcessor
    {
        private static Func<LabelSuggestion, Issue, bool> shouldLabel;

        static EventProcessor()
        {
            int threshold = 60;
            var thresholdConfig = System.Environment.GetEnvironmentVariable("ConfidenceThreshold", EnvironmentVariableTarget.Process);
            // read the configured threshold value, if present
            int.TryParse(thresholdConfig, out threshold);
            shouldLabel = (labelSuggestion, issue) =>
            {
                var topChoice = labelSuggestion.LabelScores.OrderByDescending(x => x.Score).First();
                return topChoice.Score >= threshold;
            };
        }

        public static void ProcessEvent(string eventBody, ILabelerLite labeler, ILogger _logger)
        {
            if (eventBody == "This is an event body")
            {
                _logger.LogWarning(eventBody);
                return;
            }
            string eventType = null;
            string decoded = string.Empty;
            Models.IssueEvent payload = null;
            var elementMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(eventBody);
           

            foreach (var element in elementMap)
            {
                try
                {
                    if (element.Key == "headers")
                    {
                        eventType = element.Value.EnumerateObject().First(v => v.Name == "X-GitHub-Event").Value[0].GetString();
                        _logger.LogInformation($"Received event: '{eventType}'");
                    }
                    if (element.Key == "content")
                    {
                        decoded = Encoding.UTF8.GetString(Convert.FromBase64String(element.Value.GetString()));
                        _logger.LogTrace(decoded);
                        Type webhookType = eventType switch
                        {
                            "issues" => typeof(Models.IssueEvent),
                            _ => null,
                        };
                        if (webhookType == null)
                        {
                            _logger.LogError($"Unexpected webhook type: '{eventType}'");
                            continue;
                        }
                        payload = JsonSerializer.Deserialize<Models.IssueEvent>(decoded);

                        // In order to avoid competing with other bots, we only want to respond to 'labeled' events where 
                        // where the label is "customer-reported".
                        if (payload.Action == "labeled" && payload.Label?.Name == "customer-reported")
                        {
                            // Process the issue
                            var repoInfo = payload.Repository.FullName.Split('/', 2);
                            labeler.ApplyLabelPrediction(repoInfo[0], repoInfo[1], payload.Issue.Number, shouldLabel);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    _logger.LogError(ex.StackTrace);
                    _logger.LogError(decoded);
                }
            }
        }
    }
}
