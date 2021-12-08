﻿using Hubbup.MikLabelModel;
using IssueLabeler.Shared;
using IssueLabeler.Shared.Models;
using System.Collections.Concurrent;

namespace GitHub.IssueDispatcher.Models
{
    public interface IModelHolderFactory
    {
        IModelHolder CreateModelHolder(string owner, string repo);
    }
    public class ModelHolderFactory : IModelHolderFactory
    {
        private readonly ConcurrentDictionary<(string, string), IModelHolder> _models = new ConcurrentDictionary<(string, string), IModelHolder>();
        private readonly ILogger<ModelHolderFactory> _logger;
        private IConfiguration _configuration;
        private readonly IBackgroundTaskQueue _backgroundTaskQueue;
        public ModelHolderFactory(
            ILogger<ModelHolderFactory> logger,
            IConfiguration configuration,
            IBackgroundTaskQueue backgroundTaskQueue)
        {
            _backgroundTaskQueue = backgroundTaskQueue;
            _configuration = configuration;
            _logger = logger;
        }

        public IModelHolder CreateModelHolder(string owner, string repo)
        {
            if (!IsConfigured(repo))
                return null;
            return _models.TryGetValue((owner, repo), out IModelHolder modelHolder) ?
                modelHolder :
               _models.GetOrAdd((owner, repo), InitFor(repo));
        }

        private bool IsConfigured(string repo)
        {
            // the following four configuration values are per repo values.
            string configSection = $"IssueModel:{repo}:PathPrefix";
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

        private IModelHolder InitFor(string repo)
        {
            var mh = new ModelHolder(_logger, _configuration, repo);
            if (!mh.LoadRequested)
            {
                _backgroundTaskQueue.QueueBackgroundWorkItem((ct) => mh.LoadEnginesAsync());
            }
            return mh;
        }
    }
}