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
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    [Export(typeof(PythonFileContextProvider))]
    public sealed class PythonFileContextProvider : IDisposable {
        private readonly List<PythonFileContext> _contexts;
        private readonly AsyncMutex _contextsLock = new AsyncMutex();

        public PythonFileContextProvider() {
            _contexts = new List<PythonFileContext>();
        }

        public async Task<PythonFileContext> GetOrCreateContextAsync(
            string root,
            CancellationToken cancellationToken
        ) {
            using (await _contextsLock.WaitAsync(cancellationToken)) {
                foreach (var c in _contexts) {
                    if (c.ContextRoot == root) {
                        return c;
                    }
                }
                var context = new PythonFileContext(root, null);
                _contexts.Add(context);
                return context;
            }
        }

        public async Task<IReadOnlyCollection<PythonFileContext>> GetContextsForFileAsync(
            string workspaceLocation,
            string filePath,
            CancellationToken cancellationToken
        ) {
            using (await _contextsLock.WaitAsync(cancellationToken)) {
                var res = new List<PythonFileContext>();
                foreach (var c in _contexts) {
                    if (await c.ContainsAsync(filePath, cancellationToken)) {
                        res.Add(c);
                    }
                }
                return res.AsReadOnly();
            }
        }

        public void Dispose() {
            using (_contextsLock.WaitAndDispose(1000)) {
                foreach (var c in _contexts) {
                    c.Dispose();
                }
            }
        }

        public async Task<IReadOnlyCollection<PythonFileContext>> GetOrCreateContextsForFileAsync(
            string workspaceLocation,
            string filePath,
            CancellationToken cancellationToken
        ) {
            var contexts = await GetContextsForFileAsync(
                workspaceLocation,
                filePath,
                cancellationToken
            );
            if (contexts.Any()) {
                return contexts;
            }

            if (string.IsNullOrEmpty(workspaceLocation)) {
                workspaceLocation = CommonUtils.GetParent(filePath);
            }

            if (!Directory.Exists(workspaceLocation)) {
                return null;
            }

            await FindContextsAsync(workspaceLocation, null, cancellationToken);

            return await GetContextsForFileAsync(
                workspaceLocation,
                filePath,
                cancellationToken
            );
        }


        internal async Task<IReadOnlyCollection<PythonFileContext>> GetContextsForInterpreterAsync(
            InterpreterConfiguration config,
            IProgress<string> progress,
            CancellationToken cancellationToken
        ) {
            var contexts = new List<PythonFileContext>();
            foreach (var path in config.SysPath) {
                contexts.AddRange(await GetContextsAsync(path, progress, cancellationToken));
            }
            return contexts;
        }

        private static bool IsInitPyFile(string fullPath) {
            return Path.GetFileNameWithoutExtension(fullPath)
                .Equals("__init__", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<IReadOnlyCollection<PythonFileContext>> GetContextsAsync(
            string workspaceLocation,
            IProgress<string> progress,
            CancellationToken cancellationToken
        ) {
            var contexts = new Dictionary<string, List<string>>();
            var contextMap = new Dictionary<string, List<string>>();
            var scan = new Queue<string>();

            if (Directory.Exists(workspaceLocation)) {
                scan.Enqueue(workspaceLocation);
            }

            while (scan.Any()) {
                cancellationToken.ThrowIfCancellationRequested();

                var dir = scan.Dequeue();

                try {
                    var subdirs = Directory.GetDirectories(dir);

                    foreach (var d in subdirs) {
                        //if (Path.GetFileName(d.TrimEnd(DirSeparators)).StartsWith(".")) {
                        //    // Never recurse into 'dot' directories
                        //    continue;
                        //}

                        scan.Enqueue(d);
                    }
                } catch (IOException) {
                } catch (UnauthorizedAccessException) {
                }

                try {
                    var files = Directory.GetFiles(dir, "*.py");

                    if (files.Any()) {
                        List<string> context;
                        if (!files.Any(IsInitPyFile)) {
                            // New context
                            contexts[dir] = context = new List<string>();
                        } else {
                            // Parent context
                            if (!contextMap.TryGetValue(Path.GetDirectoryName(dir), out context)) {
                                contexts[dir] = context = new List<string>();
                            }
                        }
                        contextMap[dir] = context;
                        context.AddRange(files);
                    }
                } catch (IOException) {
                } catch (UnauthorizedAccessException) {
                }
            }

            var result = new List<PythonFileContext>();

            foreach (var kv in contexts) {
                var pfc = new PythonFileContext(kv.Key, "");
                await pfc.AddDocumentsAsync(
                    kv.Value.Select(f => new FileSourceDocument(f)).ToList(),
                    cancellationToken
                );
                result.Add(pfc);
            }

            return result;
        }

        public async Task FindContextsAsync(
            string workspaceLocation,
            IProgress<string> progress,
            CancellationToken cancellationToken
        ) {
            var contexts = await GetContextsAsync(workspaceLocation, progress, cancellationToken);
            await AddContextsAsync(contexts, cancellationToken);
        }

        private async Task AddContextsAsync(
            IEnumerable<PythonFileContext> contexts,
            CancellationToken cancellationToken
        ) {
            bool any = false;
            using (await _contextsLock.WaitAsync(cancellationToken)) {
                foreach (var pfc in contexts) {
                    _contexts.Add(pfc);
                    any = true;
                }
            }

            if (any) {
                ContextsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler ContextsChanged;
    }
}
