using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace IssueLabeler.Shared.Models
{
    public interface IModelHolderFactoryLite
    {
        Task<IModelHolder> CreateModelHolder(string owner, string repo);
    }
    public class ModelHolderFactoryLite : IModelHolderFactoryLite
    {
        private readonly ConcurrentDictionary<(string, string), IModelHolder> _models = new ConcurrentDictionary<(string, string), IModelHolder>();
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

        public async Task<IModelHolder> CreateModelHolder(string owner, string repo)
        {
            IModelHolder modelHolder = null;
            if (_models.TryGetValue((owner, repo), out modelHolder))
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
                        modelHolder = await InitFor(repo);
                        _models.GetOrAdd((owner, repo), modelHolder);
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

        private bool IsConfigured(string repo)
        {
            // the following four configuration values are per repo values.
            string configSection = $"IssueModel:{repo}:BlobName";
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
            return false;
        }

        private async Task<IModelHolder> InitFor(string repo)
        {
            var mh = new ModelHolder(_logger, _configuration, repo);
            if (!mh.LoadRequested)
            {
                await mh.LoadEnginesAsync();
            }
            return mh;
        }
    }
}