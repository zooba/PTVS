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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer.Tasks;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.VisualStudioTools;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public sealed class PythonLanguageService : IDisposable {
        private readonly InterpreterConfiguration _config;
        private readonly CancellationTokenSource _disposing;
        private int _users;

        private readonly SemaphoreSlim _contextLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<PythonFileContext, ContextState> _contexts;

        private readonly SemaphoreSlim _searchPathsLock = new SemaphoreSlim(1, 1);
        private readonly List<KeyValuePair<string, string[]>> _searchPaths;

        private static readonly Regex ImportNameRegex = new Regex(
            @"^([\w_][\w\d_]+)(\.py[wcd]?)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)
        );

        public PythonLanguageService(InterpreterConfiguration config) {
            _users = 1;

            _disposing = new CancellationTokenSource();
            _config = config;

            _contexts = new Dictionary<PythonFileContext, ContextState>();
            _searchPaths = new List<KeyValuePair<string, string[]>>();
            foreach (var p in config.SysPath) {
                _searchPaths.Add(new KeyValuePair<string, string[]>(p, null));
            }
        }

        #region Lifetime Management

        internal void AddReference() {
            Interlocked.Increment(ref _users);
        }

        private void ThrowIfDisposed() {
            if (Volatile.Read(ref _users) <= 0) {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        public void Dispose() {
            if (Interlocked.Decrement(ref _users) > 0) {
                return;
            }

            _disposing.Cancel();

            // TODO: Clean up service

            foreach (var context in _contexts) {
                context.Key.Disposed -= Context_Disposed;
                context.Key.SourceDocumentContentChanged -= Context_SourceDocumentContentChanged;
                context.Value.Dispose();
            }
        }

        #endregion

        public InterpreterConfiguration Configuration {
            get { return _config; }
        }

        public async Task AddSearchPathAsync(string searchPath, string prefix, CancellationToken cancellationToken) {
            await _searchPathsLock.WaitAsync(cancellationToken);
            try {
                var prefixParts = string.IsNullOrEmpty(prefix) ? null : prefix.Split('.');
                _searchPaths.Add(new KeyValuePair<string, string[]>(searchPath, prefixParts));
            } finally {
                _searchPathsLock.Release();
            }
        }

        public async Task ClearSearchPathsAsync(CancellationToken cancellationToken) {
            await _searchPathsLock.WaitAsync(cancellationToken);
            try {
                _searchPaths.Clear();
            } finally {
                _searchPathsLock.Release();
            }
        }

        private async Task<IReadOnlyCollection<KeyValuePair<string, string[]>>> GetSearchPathsAsync(
            CancellationToken cancellationToken
        ) {
            await _searchPathsLock.WaitAsync(cancellationToken);
            try {
                return _searchPaths.ToList();
            } finally {
                _searchPathsLock.Release();
            }
        }

        public static IReadOnlyList<string> GetModuleFullNameParts(string importName, string importingFromModule) {
            var parts = importName.Split('.');
            if (parts.Length == 0) {
                return parts;
            }

            if (string.IsNullOrEmpty(parts[0])) {
                int emptyParts = parts.TakeWhile(string.IsNullOrEmpty).Count();
                var parentParts = importingFromModule.Split('.');
                parts = parentParts.Take(parentParts.Length - emptyParts).Concat(parts.Skip(emptyParts)).ToArray();
            }

            return parts;
        }

        private static IEnumerable<string> SkipMatchingLeadingElements(
            IEnumerable<string> parts,
            IEnumerable<string> elements
        ) {
            var p = parts.ToArray();
            int i = 0;
            foreach (var e in elements) {
                if (i >= p.Length || p[i] != e) {
                    return Enumerable.Empty<string>();
                }
                i += 1;
            }

            return p.Skip(i);
        }

        public async Task<string> ResolveImportAsync(
            string importName,
            string importingFromModule,
            CancellationToken cancellationToken
        ) {
            var parts = GetModuleFullNameParts(importName, importingFromModule);

            var fileParts = parts.ToArray();
            fileParts[fileParts.Length - 1] += ".py";

            var initParts = parts.ToList();
            initParts.Add("__init__.py");

            var searchPaths = await GetSearchPathsAsync(cancellationToken);

            await _contextLock.WaitAsync(cancellationToken);
            try {
                foreach (var kv in searchPaths) {
                    var rootPath = kv.Key;
                    var trimmedFileParts = kv.Value == null ?
                        fileParts :
                        SkipMatchingLeadingElements(fileParts, kv.Value).ToArray();
                    var trimmedInitParts = kv.Value == null ?
                        initParts :
                        SkipMatchingLeadingElements(initParts, kv.Value).ToList();

                    foreach (var context in _contexts.Values) {
                        AnalysisState value;
                        // TODO: Check whether .py file precedes /__init__.py
                        if (context.AnalysisStates.TryFindValueByParts(kv.Key, trimmedFileParts, out value) ||
                            context.AnalysisStates.TryFindValueByParts(kv.Key, trimmedInitParts, out value)) {
                            return value.Document.Moniker;
                        }
                    }
                }
            } finally {
                _contextLock.Release();
            }
            return null;
        }

        private async void Context_Disposed(object sender, EventArgs e) {
            var context = sender as PythonFileContext;
            if (context == null) {
                Debug.Fail("Invalid event");
                return;
            }

            context.Disposed -= Context_Disposed;

            await _contextLock.WaitAsync();
            try {
                _contexts.Remove(context);
            } finally {
                _contextLock.Release();
            }
        }

        public async Task AddFileContextAsync(PythonFileContext context, CancellationToken cancellationToken) {
            ThrowIfDisposed();

            ContextState state;

            await _contextLock.WaitAsync(cancellationToken);
            try {
                if (!_contexts.TryGetValue(context, out state)) {
                    _contexts[context] = state = new ContextState(this, context, _disposing.Token);
                    context.Disposed += Context_Disposed;
                    context.SourceDocumentContentChanged += Context_SourceDocumentContentChanged;
                }
            } finally {
                _contextLock.Release();
            }

            var docs = await context.GetDocumentsAsync(cancellationToken);

            foreach (var doc in docs) {
                AnalysisState item;
                if (!state.AnalysisStates.TryGetValue(doc.Moniker, out item)) {
                    item = new AnalysisState(doc, context);
                    state.AnalysisStates.Add(doc.Moniker, item);
                }
                //state.Enqueue(new DocumentChanged(item, doc));
            }
        }

        private async void Context_SourceDocumentContentChanged(object sender, SourceDocumentContentChangedEventArgs e) {
            var context = (PythonFileContext)sender;
            var item = await GetAnalysisStateAsync(context, e.Document.Moniker, CancellationToken.None)
                .ConfigureAwait(false);

            await _contextLock.WaitAsync(CancellationToken.None);
            try {
                ContextState state;
                if (_contexts.TryGetValue(context, out state)) {
                    state.Enqueue(new DocumentChanged(item, e.Document));
                }
            } finally {
                _contextLock.Release();
            }

        }

        private async Task<AnalysisState> GetAnalysisStateAsync(
            PythonFileContext context,
            string moniker,
            CancellationToken cancellationToken
        ) {
            AnalysisState item = null;
            await _contextLock.WaitAsync(cancellationToken);
            try {
                if (context == null) {
                    foreach (var c in _contexts.Values) {
                        if (!c.AnalysisStates.TryGetValue(moniker, out item)) {
                            continue;
                        }

                        if (item?.Version == 0) {
                            c.Enqueue(new DocumentChanged(item, item.Document));
                        }
                        return item;
                    }
                    return null;
                }

                ContextState state;
                if (!_contexts.TryGetValue(context, out state)) {
                    return null;
                }

                if (!state.AnalysisStates.TryGetValue(moniker, out item)) {
                    return null;
                }

                if (item?.Version == 0) {
                    state.Enqueue(new DocumentChanged(item, item.Document));
                }

            } finally {
                _contextLock.Release();
            }

            return item;
        }

        public async Task<Tokenization> GetTokenizationAsync(
            PythonFileContext context,
            string moniker,
            CancellationToken cancellationToken
        ) {
            var state = await GetAnalysisStateAsync(context, moniker, cancellationToken);
            if (state == null) {
                return null;
            }
            return await state.GetTokenizationAsync(cancellationToken);
        }

        public async Task<PythonAst> GetAstAsync(
            PythonFileContext context,
            string moniker,
            CancellationToken cancellationToken
        ) {
            var state = await GetAnalysisStateAsync(context, moniker, cancellationToken);
            if (state == null) {
                return null;
            }
            return await state.GetAstAsync(cancellationToken);
        }

        public async Task<IReadOnlyDictionary<string, string>> GetImportableModulesAsync(
            string importName,
            string importingFromModule,
            CancellationToken cancellationToken
        ) {
            var searchPaths = await GetSearchPathsAsync(cancellationToken);
            var parts = GetModuleFullNameParts(importName, importingFromModule);

            await _contextLock.WaitAsync(cancellationToken);
            try {
                var result = new Dictionary<string, string>();

                foreach (var searchPath in searchPaths) {
                    int skipParts = 0;
                    var p = searchPath.Value;
                    if (p != null && p.Any()) {
                        while (skipParts < p.Length && skipParts < parts.Count) {
                            if (p[skipParts] != parts[skipParts]) {
                                skipParts = -1;
                                break;
                            }
                        }
                        if (skipParts < 0) {
                            continue;
                        }
                    }

                    foreach (var state in _contexts.Values) {
                        foreach (var c in state.AnalysisStates.GetChildren(searchPath.Key, parts.Skip(skipParts))) {
                            try {
                                var m = ImportNameRegex.Match(c);
                                if (m.Success) {
                                    result[m.Groups[1].Value] = Path.Combine(searchPath.Key, c);
                                }
                            } catch (RegexMatchTimeoutException) {
                            }
                        }
                    }
                }

                return result;
            } finally {
                _contextLock.Release();
            }
        }

        public async Task<IReadOnlyDictionary<string, Variable>> GetModuleMembersAsync(
            PythonFileContext context,
            string moniker,
            string localName,
            CancellationToken cancellationToken
        ) {
            var state = await GetAnalysisStateAsync(context, moniker, cancellationToken);
            if (state == null) {
                return null;
            }

            var members = await state.GetVariablesAsync(cancellationToken);

            string prefix = null;
            if (!string.IsNullOrEmpty(localName)) {
                prefix = localName + ".";
            }
            return new DictionaryPrefixWrapper<Variable>(members, prefix, new[] { '.' });
        }


        internal async Task EnqueueAsync(
            PythonFileContext context,
            QueueItem item,
            CancellationToken cancellationToken
        ) {
            await _contextLock.WaitAsync(cancellationToken);
            try {
                ContextState state;
                if (_contexts.TryGetValue(context, out state)) {
                    state.Enqueue(item);
                }
            } finally {
                _contextLock.Release();
            }
        }

        sealed class QueueThread : IDisposable {
            private readonly Queue<QueueItem>[] _queue;
            private readonly Thread _thread;
            private readonly ManualResetEventSlim _queueChanged;
            private readonly PythonLanguageService _analyzer;
            private readonly PythonFileContext _context;
            private readonly CancellationToken _cancel;
            private ExceptionDispatchInfo _edi;

            public QueueThread(
                PythonLanguageService analyzer,
                PythonFileContext context,
                string threadName,
                CancellationToken cancellationToken
            ) {
                _queue = new Queue<QueueItem>[Enum.GetValues(typeof(ThreadPriority)).Length];
                _queueChanged = new ManualResetEventSlim();
                _analyzer = analyzer;
                _context = context;
                _cancel = cancellationToken;
                _thread = new Thread(Worker);

                var name = context.ContextRoot;
                if (!string.IsNullOrEmpty(name)) {
                    if (name.Length > 30) {
                        name = name.Substring(0, 13) + "..." + name.Substring(name.Length - 13);
                    }
                    _thread.Name = threadName + ": " + name;
                } else {
                    _thread.Name = threadName;
                }
                _thread.Start(this);
            }

            public void Dispose() {
                _queueChanged.Dispose();
                _thread.Join(1000);
                if (_edi != null) {
                    _edi.Throw();
                }
            }

            public void Enqueue(QueueItem item) {
                if (_edi != null) {
                    _edi.Throw();
                }
                lock (_queue) {
                    var q = _queue[(int)item.Priority];
                    if (q == null) {
                        _queue[(int)item.Priority] = q = new Queue<QueueItem>();
                    }
                    q.Enqueue(item);
                    _queueChanged.Set();
                }
            }

            private static void Worker(object o) {
                var thread = (QueueThread)o;
                try {
                    bool exit = false;
                    while (!exit) {
                        exit = !thread.QueueWorker();
                    }
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    thread._edi = ExceptionDispatchInfo.Capture(ex);
                }
            }

            private bool QueueWorker() {
                try {
                    _queueChanged.Wait(_cancel);
                    _cancel.ThrowIfCancellationRequested();
                    _queueChanged.Reset();
                } catch (ObjectDisposedException) {
                    throw new OperationCanceledException();
                }

                QueueItem item;
                while (true) {
                    lock (_queue) {
                        item = null;
                        for (int i = _queue.Length - 1; i >= 0; --i) {
                            var q = _queue[i];
                            if (q != null && q.Any()) {
                                item = q.Dequeue();
                                break;
                            }
                        }
                        if (item == null) {
                            break;
                        }
                    }
                    item.PerformAsync(_analyzer, _context, _cancel).GetAwaiter().GetResult();
                }
                return true;
            }
        }

        private struct ContextState {
            public readonly PathSet<AnalysisState> AnalysisStates;
            public readonly QueueThread Thread;

            public ContextState(
                PythonLanguageService analyzer,
                PythonFileContext context,
                CancellationToken disposalToken
            ) {
                AnalysisStates = new PathSet<AnalysisState>(context.ContextRoot);
                
                Thread = new QueueThread(analyzer, context, "Analysis", disposalToken);
            }

            public void Dispose() {
                Thread.Dispose();
            }

            internal void Enqueue(QueueItem item) {
                Thread.Enqueue(item);
            }
        }
    }
}
