// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using IssueLabeler.Shared;
using Octokit;
using System.Threading.Tasks;
using System;

namespace Hubbup.MikLabelModel
{
    public interface ILabelerLite
    {
        Task ApplyLabelPrediction(string owner, string repo, int number, Func<LabelSuggestion, Issue, float, bool> shouldApplyLabel);
    }
}