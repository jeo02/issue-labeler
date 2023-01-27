using System;
using Hubbup.MikLabelModel;
using Microsoft.Extensions.Configuration;

namespace IssueLabeler
{
	public abstract class IssueLabelerFunctionBase
	{
        protected ILabelerLite Labeler { get; }
        protected IConfiguration Config { get; }

        public IssueLabelerFunctionBase(ILabelerLite labeler, IModelHolderFactoryLite modelHolderFactory, IConfiguration config)
        {
            Labeler = labeler;
            Config = config;

            var owner = config["RepoOwner"];
            var repos = config["RepoNames"];

            if (repos != null)
            {
                var r = repos.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var repo in r)
                {
                    var blobConfig = $"IssueModel:{repo}:BlobConfigNames";
                    if (!string.IsNullOrEmpty(config[blobConfig]))
                    {
                        var blobConfigs = config[blobConfig].Split(';', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var blobConfigName in blobConfigs)
                        {
                            modelHolderFactory.CreateModelHolder(owner, repo, blobConfigName).GetAwaiter().GetResult();
                        }
                    }
                    else
                    {
                        modelHolderFactory.CreateModelHolder(owner, repo).GetAwaiter().GetResult();
                    }
                }
            }
        }
    }
}

