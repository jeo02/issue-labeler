using System;
using Hubbup.MikLabelModel;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(IssueLabeler.Startup))]

namespace IssueLabeler
{
    internal class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var config = builder.GetContext().Configuration;

            builder.Services
                 .AddLogging()
                 .AddSingleton(config)
                 .AddSingleton<IModelHolderFactoryLite, ModelHolderFactoryLite>()
                 .AddSingleton<ILabelerLite, LabelerLite>();
        }
    }
}
