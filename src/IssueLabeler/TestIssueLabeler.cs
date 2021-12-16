using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Hubbup.MikLabelModel;
using System.Text.Json;
using System.Linq;
using IssueLabeler.Shared.Models;
using Microsoft.Extensions.Configuration;

namespace IssueLabeler
{
    public class TestIssueLabeler
    {
        private static Func<Shared.LabelSuggestion, Octokit.Issue, bool> shouldUpdate = new Func<Shared.LabelSuggestion, Octokit.Issue, bool>((sug, _) => sug.LabelScores.Any(s => s.Score > 0.5f));
        private readonly ILabelerLite _labeler;

        public TestIssueLabeler(ILabelerLite labeler)
        {
            _labeler = labeler;
        }

        [FunctionName("TestIssueLabeler")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<WebHookModel>(requestBody);
            log.LogInformation($"TestIssueLabeler invoked with message: {requestBody}");
            await _labeler.ApplyLabelPrediction(data.owner, data.repo, data.id, shouldUpdate);
            return new OkObjectResult("success");
        }
    }
}
