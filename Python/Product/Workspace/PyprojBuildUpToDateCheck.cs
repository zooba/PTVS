// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;

namespace Microsoft.PythonTools.Workspace {
    [ExportBuildUpToDateCheck(
        BuildUpToDateCheckProviderOptions.None,
        ProviderType,
        new[] { FileExtension }
    )]
    public sealed class PyprojBuildUpToDateCheckFactory : IWorkspaceProviderFactory<IBuildUpToDateCheckProvider> {
        public const string ProviderType = "{7BC0F091-9B3F-44A5-9839-B747D8DB4D9D}";
        private const string FileExtension = ".pyproj";

        public IBuildUpToDateCheckProvider CreateProvider(IWorkspace workspaceContext) {
            return new PyprojBuildUpToDateCheckProvider(workspaceContext);
        }
    }

    class PyprojBuildUpToDateCheckProvider : IBuildUpToDateCheckProvider {
        private IWorkspace _workspace;

        public PyprojBuildUpToDateCheckProvider(IWorkspace workspace) {
            _workspace = workspace;
        }

        public async Task<bool> IsUpToDateAsync(string projectFile, string projectFileTarget, IBuildConfigurationContext buildConfigurationContext, string buildConfiguration, CancellationToken cancellationToken = default(CancellationToken)) {
            var data = await _workspace.GetIndexWorkspaceService().GetFileDataValuesAsync<IReadOnlyDictionary<string, object>>(
                projectFile,
                PyprojFileScannerProvider.ProviderGuid,
                cancellationToken: cancellationToken
            );

            foreach(var d in data.MaybeEnumerate()) {
                object o;
                IReadOnlyList<string> targets;
                if (d.Value?.TryGetValue("Targets", out o) ?? false &&
                    (targets = o as IReadOnlyList<string>) != null &&
                    targets.Contains("CoreCompile")) {
                    // Assume never up to date if there is a build step
                    return false;
                }
            }

            // Assume always up to date if builds are not supported
            return true;
        }
    }
}
