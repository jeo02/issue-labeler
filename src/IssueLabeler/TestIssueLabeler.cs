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
using Octokit;

namespace IssueLabeler
{
    public class TestIssueLabeler
    {
        private static Func<Shared.LabelSuggestion, Issue, float, bool> shouldUpdate = new ((sug, _, threshold) => sug.LabelScores.Any(s => s.Score > threshold));
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
            WebHookModel data = null;
            if (req.Method == "POST")
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                data = JsonSerializer.Deserialize<WebHookModel>(requestBody);
                log.LogInformation($"TestIssueLabeler invoked with message: {requestBody}");
            }
            else
            {
                data = new WebHookModel() { id = 23600, owner = "Azure", repo = "azure-sdk-for-net" };
            }

            await _labeler.ApplyLabelPrediction(data.owner, data.repo, data.id, shouldUpdate);
            return new OkObjectResult("success");
        }
    }
}
