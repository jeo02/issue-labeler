using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Hubbup.MikLabelModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IssueLabeler
{
    public class AzureSdkIssueLabelerService : IssueLabelerFunctionBase
    {
        public AzureSdkIssueLabelerService(ILabelerLite labeler, IModelHolderFactoryLite modelHolderFactory, IConfiguration config)
            : base(labeler, modelHolderFactory, config)
        {
        }

        [FunctionName("AzureSdkIssueLabelerService")]
        public async Task<PredictionResponse> Run([HttpTrigger(AuthorizationLevel.Function, "POST", Route = null)] HttpRequest request, ILogger log)
        {
            using var bodyReader = new StreamReader(request.Body);

            var requestBody = await bodyReader.ReadToEndAsync();
            var issue = JsonConvert.DeserializeObject<IssuePayload>(requestBody);

            // In order for labels to be valid for Azure SDK use, there must
            // be at least two of them, which corresponds to a Service (pink)
            // and Category (yellow).  If that is not met, then no predictions
            // should be returned.
            var predictions = await Labeler.QueryLabelPrediction(
                issue.IssueNumber,
                issue.Title,
                issue.Body,
                issue.IssueUserLogin,
                issue.RepositoryName,
                issue.RepositoryOwnerName);

            if (predictions.Count < 2)
            {
                return new PredictionResponse(Array.Empty<string>());
            }

            return new PredictionResponse(predictions);
        }

        // Private type used for deserializing the request paylod of issue data.
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