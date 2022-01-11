using Hubbup.MikLabelModel;
using IssueLabeler.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Hubbup.MikLabelModel
{
    public interface IModelHolderFactoryLite
    {
        Task<IModelHolder> CreateModelHolder(string owner, string repo, string modelBlobConfigName = null);
        Task<IPredictor> GetPredictor(string owner, string repo, string modelBlobConfigName = null);
    }
    public class ModelHolderFactoryLite : IModelHolderFactoryLite
    {
        private readonly ConcurrentDictionary<(string, string, string), IModelHolder> _models = new ConcurrentDictionary<(string, string, string), IModelHolder>();
        private readonly ILogger<ModelHolderFactoryLite> _logger;
        private IConfiguration _configuration;
        private SemaphoreSlim _sem = new SemaphoreSlim(1);

        public ModelHolderFactoryLite(
            ILogger<ModelHolderFactoryLite> logger,
            IConfiguration configuration)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IModelHolder> CreateModelHolder(string owner, string repo, string modelBlobConfigName = null)
        {
            IModelHolder modelHolder = null;
            if (_models.TryGetValue((owner, repo, modelBlobConfigName), out modelHolder))
            {
                return modelHolder;
            }

            if (IsConfigured(repo))
            {
                bool acquired = false;
                try
                {
                    await _sem.WaitAsync();
                    acquired = true;
                    if (acquired)
                    {
                        modelHolder = await InitFor(repo, modelBlobConfigName);
                        _models.GetOrAdd((owner, repo, modelBlobConfigName), modelHolder);
                    }
                }
                finally
                {
                    if (acquired)
                    {
                        _sem.Release();
                    }
                }
            }
            return modelHolder;
        }

        public async Task<IPredictor> GetPredictor(string owner, string repo, string modelBlobConfigName = null)
        {
            var modelHolder = await CreateModelHolder(owner, repo, modelBlobConfigName);
            if (modelHolder == null)
            {
                throw new InvalidOperationException($"Repo {owner}/{repo} is not yet configured for label prediction.");
            }
            if (!modelHolder.IsIssueEngineLoaded || (!modelHolder.UseIssuesForPrsToo && !modelHolder.IsPrEngineLoaded))
            {
                throw new InvalidOperationException("Issue engine must be loaded.");
            }
            return new Predictor(_logger, modelHolder) { ModelName = modelBlobConfigName };
        }

        private bool IsConfigured(string repo)
        {
            // the following four configuration values are per repo values.
            string configSection = $"IssueModel:{repo}:BlobConfigNames";
            if (string.IsNullOrEmpty(_configuration[configSection]))
            {
                configSection = $"IssueModel:{repo}:BlobName";
                if (!string.IsNullOrEmpty(_configuration[configSection]))
                {
                    configSection = $"IssueModel:{repo}:BlobName";
                    if (!string.IsNullOrEmpty(_configuration[configSection]))
                    {
                        configSection = $"PrModel:{repo}:PathPrefix";
                        if (!string.IsNullOrEmpty(_configuration[configSection]))
                        {
                            // has both pr and issue config - allowed
                            configSection = $"PrModel:{repo}:BlobName";
                            return !string.IsNullOrEmpty(_configuration[configSection]);
                        }
                        else
                        {
                            // has issue config only - allowed
                            configSection = $"PrModel:{repo}:BlobName";
                            return string.IsNullOrEmpty(_configuration[configSection]);
                        }
                    }
                }
            }
            else { return true; }
            return false;
        }

        private async Task<IModelHolder> InitFor(string repo, string modelBlobConfigName = null)
        {
            var mh = new ModelHolder(_logger, _configuration, repo, modelBlobConfigName);
            if (!mh.LoadRequested)
            {
                await mh.LoadEnginesAsync();
            }
            return mh;
        }
    }
}