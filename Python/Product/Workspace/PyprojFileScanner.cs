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
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Project;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Extensions;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace Microsoft.PythonTools.Workspace {
    [ExportFileScanner(
        (FileScannerOptions)(SolutionWorkspaceProviderOptions.SupportedAndOnlySolutionWorkspace),
        ProviderType,
        "PyProj",
        new[] { ".pyproj" },
        new[] { typeof(IReadOnlyDictionary<string, object>) },
        ProviderPriority.Normal
    )]
    public sealed class PyprojFileScannerProvider : IWorkspaceProviderFactory<IFileScanner> {
        const string ProviderType = "4B13B458-3E7A-4BFE-B314-6753688F4298";
        public static readonly Guid ProviderGuid = new Guid(ProviderType);


        [Import(typeof(SVsServiceProvider))]
        public IServiceProvider _site = null;

        public IFileScanner CreateProvider(IWorkspace workspaceContext) {
            return new PyprojFileScanner(workspaceContext, _site);
        }
    }

    class PyprojFileScanner : IFileScanner {
        private readonly IWorkspace _workspace;
        private readonly IServiceProvider _site;

        public PyprojFileScanner(IWorkspace workspaceContext, IServiceProvider site) {
            _workspace = workspaceContext;
            _site = site;
        }

        public async Task<T> ScanContentAsync<T>(
            string filePath,
            CancellationToken cancellationToken
        ) where T : class {
            if (!typeof(T).IsEquivalentTo(typeof(IReadOnlyDictionary<string, object>))) {
                throw new NotImplementedException();
            }

            var configs = await _workspace.JTF.RunAsync(VsTaskRunContext.UIThreadNormalPriority, async () => {
                var svc = _site.GetComponentModel().GetService<IInterpreterRegistryService>();
                return svc.Configurations.ToArray();
            });

            var content = new Dictionary<string, object>();

            string projectHome = null;
            LaunchConfiguration config = null;
            IReadOnlyDictionary<string, IReadOnlyList<string>> files = null;
            IReadOnlyList<string> targets = null;

            try {
                await _workspace.JTF.RunAsync(VsTaskRunContext.UIThreadNormalPriority, async () => {
                    await _workspace.JTF.SwitchToMainThreadAsync(cancellationToken);
                    PythonProjectPackage.ReadProjectFile(
                        _site,
                        filePath,
                        out projectHome,
                        out config,
                        out files,
                        out targets
                    );
                });
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Debug.Fail(ex.ToUnhandledExceptionMessage(GetType()));
                return null;
            }

            if (config != null) {
                content["LaunchConfiguration"] = config;
            }
            if (!string.IsNullOrEmpty(projectHome)) {
                content["ProjectHome"] = projectHome;
            }
            if (files != null) {
                content["Items"] = files;
            }
            if (targets != null) {
                content["Targets"] = targets;
            }

            return content as T;
        }
    }
}
