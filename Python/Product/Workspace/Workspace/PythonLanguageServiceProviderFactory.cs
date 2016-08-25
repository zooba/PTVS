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
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Intellisense;

namespace Microsoft.PythonTools.Workspace {
    [ExportFileContextProvider(ProviderType, ProviderPriority.Normal, PythonFileContext.ContextType)]
    class PythonLanguageServiceProviderFactory : IWorkspaceProviderFactory<ILanguageServiceProvider> {
        public const string ProviderType = "1B8A30A0-A9C3-40F2-999A-DBB9F8571AA9";

        public ILanguageServiceProvider CreateProvider(IWorkspace workspaceContext) {
            return new PythonLanguageServiceProvider(workspaceContext);
        }
    }
}
