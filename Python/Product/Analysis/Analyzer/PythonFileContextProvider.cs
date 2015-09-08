using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    [Export(typeof(PythonFileContextProvider))]
    public sealed class PythonFileContextProvider {
        private readonly List<PythonFileContext> _contexts;
        private readonly SemaphoreSlim _contextsLock = new SemaphoreSlim(1, 1);

        public PythonFileContextProvider() {
            _contexts = new List<PythonFileContext>();
        }

        public async Task<IReadOnlyCollection<PythonFileContext>> GetContextsForFileAsync(
            string workspaceLocation,
            string filePath,
            CancellationToken cancellationToken
        ) {
            var res = new List<PythonFileContext>();
            await _contextsLock.WaitAsync(cancellationToken);
            try {
                foreach (var c in _contexts) {
                    if (await c.ContainsAsync(filePath, cancellationToken)) {
                        res.Add(c);
                    }
                }
                return res.AsReadOnly();
            } finally {
                _contextsLock.Release();
            }
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
            scan.Enqueue(workspaceLocation);
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

            await _contextsLock.WaitAsync(cancellationToken);
            try {
                foreach(var pfc in contexts) {
                    _contexts.Add(pfc);
                }
            } finally {
                _contextsLock.Release();
            }
        }
    }
}
