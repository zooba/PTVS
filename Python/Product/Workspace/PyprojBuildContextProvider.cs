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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Extensions;
using Microsoft.VisualStudio.Workspace.Extensions.Build;

namespace Microsoft.PythonTools.Workspace {
    [ExportFileContextProvider(
        (FileContextProviderOptions)SolutionWorkspaceProviderOptions.Supported,
        ProviderType,
        ProviderPriority.Normal,
        new Type[0],
        BuildActionContext.ContextType)]
    sealed class PyprojBuildContextProviderFactory : IWorkspaceProviderFactory<IFileContextProvider> {
        const string ProviderType = "FA1831B3-0639-4152-9C7D-C1D0668D36F3";
        public static readonly Guid ProviderTypeGuid = new Guid(ProviderType);

        [Import(typeof(SVsServiceProvider))]
        public IServiceProvider _site = null;

        public IFileContextProvider CreateProvider(IWorkspace workspaceContext) {
            return new PyprojBuildContextProvider(workspaceContext, _site);
        }
    }

    class PyprojBuildContextProvider : IFileContextProvider {
        private readonly IWorkspace _workspace;
        private readonly IServiceProvider _site;

        public PyprojBuildContextProvider(IWorkspace workspace, IServiceProvider site) {
            _workspace = workspace;
            _site = site;
        }

        private static bool IsPyprojFile(string path) {
            return ".pyproj".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken) {
            if (IsPyprojFile(filePath)) {
                return new[] {
                    new FileContext(
                        PyprojBuildContextProviderFactory.ProviderTypeGuid,
                        BuildActionContext.ContextTypeGuid,
                        new BuildActionContext("notepad.exe"),
                        new [] { filePath },
                        "Notepad"
                    )
                };
            }
            return FileContext.EmptyFileContexts;
        }
    }
}
