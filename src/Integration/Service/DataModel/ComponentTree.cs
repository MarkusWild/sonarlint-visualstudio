//-----------------------------------------------------------------------
// <copyright file="ComponentTree.cs" company="SonarSource SA and Microsoft Corporation">
//   Copyright (c) SonarSource SA and Microsoft Corporation.  All rights reserved.
//   Licensed under the MIT License. See License.txt in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using Newtonsoft.Json;

namespace SonarLint.VisualStudio.Integration.Service
{
    internal class ComponentTree
    {
        [JsonProperty("paging")]
        public Paging Paging { get; set; }

        [JsonProperty("components")]
        public ComponentInformation[] Components { get; set; }
    }
}
