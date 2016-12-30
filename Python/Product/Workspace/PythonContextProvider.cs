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

//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.VisualStudio.Workspace;
//using Microsoft.PythonTools.Intellisense;

//namespace Microsoft.PythonTools.Workspace {
//    [ExportFileContextProvider(ProviderType, ProviderPriority.Normal, )]
//    public sealed class PythonContextProviderFactory : IWorkspaceProviderFactory<IFileContextProvider> {
//        public const string ProviderType = "{DDBE79D3-BB9A-4F13-BC49-5B1C2A4B0844}";
//        public static readonly Guid ProviderGuid = new Guid(ProviderType);

//        private readonly ConcurrentDictionary<IWorkspace, IFileContextProvider> _cache =
//            new ConcurrentDictionary<IWorkspace, IFileContextProvider>();

//        public IFileContextProvider CreateProvider(IWorkspace workspaceContext) {
//            IFileContextProvider result;
//            if (!_cache.TryGetValue(workspaceContext, out result)) {
//                result = new PythonContextProvider(workspaceContext);
//                if (!_cache.TryAdd(workspaceContext, result)) {
//                    result = _cache[workspaceContext];
//                }
//            }
//            return result;
//        }
//    }

//    public sealed class PythonContextProvider : IFileContextProvider {
//        private readonly IWorkspace _workspace;

//        private readonly VsProjectAnalyzer _analyzer;
//        private readonly ConcurrentDictionary<string, FileContext> _contexts;

//        public PythonContextProvider(IWorkspace workspace) {
//            _workspace = workspace;
//            _contexts = new ConcurrentDictionary<string, FileContext>();
//        }

//        public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken) {
//            FileContext context;
//            if (_contexts.TryGetValue(filePath, out context)) {
//                return new[] { context };
//            }

//            context = new FileContext(PythonContextProviderFactory.ProviderGuid, Guid.Empty, null, null);
//            while (!_contexts.TryAdd(filePath, context)) {
//                if (_contexts.TryGetValue(filePath, out context)) {
//                    break;
//                }
//            }
//            return new[] { context };
//        }
//    }
//}
