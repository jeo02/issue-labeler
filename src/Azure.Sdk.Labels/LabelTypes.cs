// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Reflection;
using CreateMikLabelModel.Models;
using Octokit;

namespace Azure.Sdk.LabelTrainer
{
    internal static class LabelTypes
    {
        public static Func<object, bool> AllTypesFilter = label => Types.All(type => type.FilterPredicate(label));

        public static LabelType[] Types { get; } = new[]
        {
            new LabelType("Service", IsServiceLabel),
            new LabelType("Category", IsCategoryLabel)
        };

        private static bool IsServiceLabel(object label) =>
           IsServiceColor(label switch
           {
               Label octo => octo.Color,
               LabelsNode node => node.Color,
                _ => GetLabelMember<string>("Color", label)
            });

        private static bool IsCategoryLabel(object label) =>
           IsCategoryColor(label switch
           {
               Label octo => octo.Color,
               LabelsNode node => node.Color,
                _ => GetLabelMember<string>("Color", label)
            });

        private static T GetLabelMember<T>(string memberName, object label)
        {
            var labelType = label.GetType();
            var colorProperty = labelType.GetProperty(memberName,  BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty);

            if (colorProperty != null)
            {
               return (T)colorProperty.GetValue(label);
            }

            var colorField = labelType.GetField(memberName,  BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetField);

            if (colorField != null)
            {
                return (T)colorField.GetValue(label);
            }

            throw new ArgumentException($"The label type: '{ labelType.Name }' does not have a '{ memberName }' member.", nameof(label));
        }

        private static bool IsServiceColor(string colorCode) =>
            string.Equals(colorCode, "e99695", StringComparison.InvariantCultureIgnoreCase);

        private static bool IsCategoryColor(string colorCode) =>
            string.Equals(colorCode, "ffeb77", StringComparison.InvariantCultureIgnoreCase);
    }
}
