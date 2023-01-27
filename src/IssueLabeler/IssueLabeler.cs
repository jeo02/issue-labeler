using Hubbup.MikLabelModel;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Linq;
using Azure.Messaging.EventHubs;
using System.Diagnostics;
using IssueLabeler.Shared.Models;
using Microsoft.Extensions.Configuration;
using System.Diagnostics.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace IssueLabeler
{
    public class IssueLabeler : IssueLabelerFunctionBase
    {
        public IssueLabeler(ILabelerLite labeler, IModelHolderFactoryLite modelHolderFactory, IConfiguration config)
            : base(labeler, modelHolderFactory, config)
        {
        }

        [FunctionName("IssueLabeler")]
        public async Task Run([EventHubTrigger("%EventHubName%", Connection = "EventHubConnection")] EventData[] events, ILogger log)
        {
            var exceptions = new List<Exception>();
            foreach (EventData eventData in events)
            {
                try
                {
                    string eventBody = Encoding.UTF8.GetString(eventData.EventBody);
                    EventProcessor.ProcessEvent(eventBody, Labeler, log, Config);

                    // Replace these two lines with your processing logic.
                    log.LogTrace($"C# Event Hub trigger function processed a message: {eventBody}");
                    await Task.Yield();
                }
                catch (Exception e)
                {
                    // We need to keep processing the rest of the batch - capture this exception and continue.
                    // Also, consider capturing details of the message that failed processing so it can be processed again later.
                    exceptions.Add(e);
                }
            }

            // Once processing of the batch is complete, if any messages in the batch failed processing throw an exception so that there is a record of the failure.
            if (exceptions.Count > 1)
                throw new AggregateException(exceptions);

            if (exceptions.Count == 1)
                throw exceptions.Single();
        }
    }
}
