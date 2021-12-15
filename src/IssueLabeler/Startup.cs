using Hubbup.MikLabelModel;
using IssueLabeler.Shared;
using IssueLabeler.Shared.Models;
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
                .AddSingleton(new GitHubClientFactory(config))
                .AddSingleton<IModelHolderFactoryLite, ModelHolderFactoryLite>()
                .AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>()
                .AddSingleton<ILabelerLite, LabelerLite>();
        }
    }
}
