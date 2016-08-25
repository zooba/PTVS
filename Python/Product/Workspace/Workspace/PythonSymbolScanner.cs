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
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace Microsoft.PythonTools.Workspace {
    [ExportFileScanner(ProviderType, "Python", new[] { ".py", ".pyw", ".pyx" }, new[] { typeof(IReadOnlyCollection<SymbolDefinition>) })]
    class PythonSymbolScanner : IWorkspaceProviderFactory<IFileScanner>, IFileScanner {
        public const string ProviderType = "3E6F8CCC-AEE9-458B-B74B-A83D044A1468";

        public IFileScanner CreateProvider(IWorkspace workspaceContext) {
            return this;
        }

        public async Task<T> ScanContentAsync<T>(string filePath, CancellationToken cancellationToken) where T : class {
            var res = new List<SymbolDefinition>();

            // TODO: Parse symbols

            return res as T;
        }
    }
}
