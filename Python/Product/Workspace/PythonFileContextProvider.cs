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
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Workspace;

namespace Microsoft.PythonTools.Workspace {
    class PythonFileContextProvider : IFileContextProvider, IDisposable {
        private readonly IWorkspace _workspace;
        private readonly Dictionary<string, FileContext> _contexts;

        private Task _findContexts;
        private CancellationTokenSource _findContextsCancel;

        public PythonFileContextProvider(IWorkspace workspace) {
            _workspace = workspace;
            _contexts = new Dictionary<string, FileContext>(StringComparer.OrdinalIgnoreCase);
        }

        public event EventHandler ContextsChanged;

        public void Dispose() {
            _findContextsCancel?.Cancel();
            foreach (var c in _contexts) {
                c.Value.GetPythonContext()?.Dispose();
            }
        }


        public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(
            string filePath,
            CancellationToken cancellationToken
        ) {
            if (_findContexts == null) {
                _findContextsCancel = new CancellationTokenSource();
                _findContexts = FindContextsAsync(_findContextsCancel.Token);
            }

            await _findContexts;

            lock (_contexts) {
                var res = new List<FileContext>();
                foreach (var c in _contexts) {
                    if (c.Value.GetPythonContext()?.Contains(filePath) ?? false) {
                        res.Add(c.Value);
                    }
                }
                return res;
            }
        }

        private async Task FindContextsAsync(CancellationToken cancel) {
            var contexts = await GetContextsWorkerAsync(cancel);

            lock (_contexts) {
                foreach (var context in contexts) {
                    cancel.ThrowIfCancellationRequested();
                    _contexts.Add(context.GetPythonContext().PackageName, context);
                }
            }
            ContextsChanged?.Invoke(this, EventArgs.Empty);
        }

        private static bool IsInitPyFile(string fullPath) {
            return Path.GetFileNameWithoutExtension(fullPath)
                .Equals("__init__", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, PathSet<ISourceDocument>> GetPackages(PathSet<ISourceDocument> set) {
            // TODO: Split up set into packages
            return new Dictionary<string, PathSet<ISourceDocument>> {
                { "", set }
            };
        }

        private async Task<IReadOnlyCollection<FileContext>> GetContextsWorkerAsync(
            CancellationToken cancellationToken
        ) {
            var contexts = new Dictionary<string, List<string>>();
            var contextMap = new Dictionary<string, List<string>>();
            var scan = new Queue<string>();

            var files = new PathSet<ISourceDocument>(_workspace.Location);
            await _workspace.GetFindFilesService().FindFilesAsync(
                // TODO: Better query
                "*.py",
                files.GetAdder(f => new FileSourceDocument(f)),
                cancellationToken
            );


            var result = new List<FileContext>();

            foreach (var kv in GetPackages(files)) {
                var pfc = new PythonFileContext(kv.Key, kv.Value);
                await pfc.AddDocumentsAsync(
                    kv.Value.GetValues().ToList(),
                    cancellationToken
                );

                result.Add(new FileContext(
                    PythonFileContextProviderFactory.ProviderGuid,
                    PythonFileContext.ContextGuid,
                    pfc,
                    kv.Value.GetPaths().ToArray()
                ));
            }

            return result;
        }
    }
}
