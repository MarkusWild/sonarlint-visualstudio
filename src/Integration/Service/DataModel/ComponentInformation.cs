//-----------------------------------------------------------------------
// <copyright file="ComponentInformation.cs" company="SonarSource SA and Microsoft Corporation">
//   Copyright (c) SonarSource SA and Microsoft Corporation.  All rights reserved.
//   Licensed under the MIT License. See License.txt in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using Newtonsoft.Json;
using System;
using System.Diagnostics;

namespace SonarLint.VisualStudio.Integration.Service
{
    [DebuggerDisplay("Key: {Key}")]
    internal class ComponentInformation
    {
        public static readonly StringComparer KeyComparer = StringComparer.Ordinal;

        [JsonProperty("key")]
        public string Key { get; set; }
    }
}
