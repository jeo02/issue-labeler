// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using CreateMikLabelModel;
using System.Threading.Tasks;

namespace Azure.Sdk.LabelTrainer
{
    /// <summary>
    ///   Serves as the main entry point for the application.
    /// </summary>
    ///
    public static class Program
    {
        /// <summary>The file to write <see cref="Trace" /> output to; the current directory is assumed.</summary>
        private const string TraceLogFilename = "trace.log";

        /// <summary>
        ///   This utility will train a set of machine learning models intended to help with prediction of the
        ///   labels that should be added to GitHub items for basic categorization and routing.
        /// </summary>
        ///
        /// <param name="repository">The full path for the repository to train.</param>
        /// <param name="gitHubToken">The access token to use for interacting with GitHub.</param>
        ///
        /// <example>
        ///   <code>
        ///     dotnet run -- --repository "Azure/azure-sdk-for-net"  --git-hub-token "[[ TOKEN ]]"
        ///   </code>
        /// </example>
        ///
        public static async Task<int> Main(string repository, string gitHubToken)
        {
            if ((string.IsNullOrEmpty(repository)) || (string.IsNullOrEmpty(gitHubToken)))
            {
                Console.WriteLine("");
                Console.WriteLine("The repository path and GitHub access token must be specified.");
                Console.WriteLine("");
                Console.WriteLine("Usage:");
                Console.WriteLine("\tdotnet run -- --repository \"Azure/azure-sdk-for-net\" --git-hub-token \"[[ TOKEN ]]\"");
                Console.WriteLine("");

                return -1;
            }

            // ==============================================================================
            //  TODO: FIGURE OUT WHY TRACE ISN'T BEING PICKED UP ACCROSS LIBRARY BOUNDARIES
            // ==============================================================================


            // Training output is communicated using Trace diagnostics.  Create listeners for
            // capturing in a log file and echoing to the command line.

            using var fileTraceListener = new TextWriterTraceListener(Path.Combine(Environment.CurrentDirectory, TraceLogFilename));
            using var consoleTraceListener = new ConsoleTraceListener();

            // Query GitHub to build the set of training data.

            var trainer = new LabelModelTrainer(gitHubToken);

            // Step 1: Download the common set of training items (issues and pull requests) for all interested labels.

            await trainer.PrepareTrainingSet(gitHubToken, Environment.CurrentDirectory, LabelTypes.AllTypesFilter).ConfigureAwait(false);

            // Each type of label needs to be trained separately.

            foreach (var labelType in LabelTypes.Types)
            {
                // Step 2: Segment the training items into discrete sets for training, validating, and testing the model.
                    // TODO: Implement on `LabelModelTrainer` and invoke here.

                // Step 3: Train the model.
                    // TODO: Create an `MLHelper`and invoke `Train` for issues and then again for PRs.

                // Step 4: Test the model.
                    // TODO: Create (or reuse) an `MLHelper` and invoke `Test` for issues and then again for PRs.
            }

            return 0;
        }
    }
}