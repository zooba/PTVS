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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer.Tasks;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public sealed class PythonLanguageService : IDisposable {
        private readonly PythonLanguageServiceProvider _provider;
        private readonly InterpreterConfiguration _config;
        private readonly CancellationTokenSource _disposing;
        private int _users;

        private readonly AsyncMutex _contextLock = new AsyncMutex();
        private readonly Dictionary<PythonFileContext, ContextState> _contexts;

        private readonly AsyncMutex _searchPathsLock = new AsyncMutex();
        private readonly List<KeyValuePair<string, string[]>> _searchPaths;

        private static readonly Regex ImportNameRegex = new Regex(
            @"^([\w_][\w\d_]+)(\.py[wcd]?)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)
        );

        internal static readonly IReadOnlyCollection<AnalysisValue> EmptyAnalysisValues = new AnalysisValue[0];

        public PythonLanguageService(PythonLanguageServiceProvider provider, InterpreterConfiguration config) {
            _provider = provider;
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

        internal bool AddReference() {
            while (true) {
                int users = Volatile.Read(ref _users);
                if (users == 0) {
                    return false;
                }
                int oldValue = Interlocked.CompareExchange(ref _users, users + 1, users);
                if (oldValue == users) {
                    return true;
                }
            }
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

            _disposing.CancelAfter(1000);
            try {
                _provider.RemoveAsync(this, _disposing.Token).Wait(_disposing.Token);
            } catch (AggregateException ae) when (ae.InnerException is OperationCanceledException) {
            } catch (OperationCanceledException) {
            }
            if (!_disposing.IsCancellationRequested) {
                _disposing.Cancel();
            }

            // TODO: Clean up service

            foreach (var context in _contexts) {
                context.Key.Disposed -= Context_Disposed;
                context.Key.DocumentsChanged -= Context_DocumentsChanged;
                context.Key.SourceDocumentContentChanged -= Context_SourceDocumentContentChanged;
                context.Value.Dispose();
            }

            _contextLock.Dispose();
            _searchPathsLock.Dispose();
            _disposing.Dispose();
        }

        #endregion

        public InterpreterConfiguration Configuration {
            get { return _config; }
        }

        public async Task AddSearchPathAsync(string searchPath, string prefix, CancellationToken cancellationToken) {
            using (await _searchPathsLock.WaitAsync(cancellationToken)) {
                var prefixParts = string.IsNullOrEmpty(prefix) ? null : prefix.Split('.');
                _searchPaths.Add(new KeyValuePair<string, string[]>(searchPath, prefixParts));
            }
        }

        public async Task ClearSearchPathsAsync(CancellationToken cancellationToken) {
            using (await _searchPathsLock.WaitAsync(cancellationToken)) {
                _searchPaths.Clear();
            }
        }

        private async Task<IReadOnlyCollection<KeyValuePair<string, string[]>>> GetSearchPathsAsync(
            CancellationToken cancellationToken
        ) {
            using (await _searchPathsLock.WaitAsync(cancellationToken)) {
                return _searchPaths.ToList();
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
            if (string.IsNullOrEmpty(importName)) {
                return null;
            }

            var parts = GetModuleFullNameParts(importName, importingFromModule);

            var fileParts = parts.ToArray();
            if (!fileParts.Any()) {
                return null;
            }

            fileParts[fileParts.Length - 1] += ".py";

            var initParts = parts.ToList();
            initParts.Add("__init__.py");

            var searchPaths = await GetSearchPathsAsync(cancellationToken);

            using (await _contextLock.WaitAsync(cancellationToken)) {
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

            using (_contextLock.WaitAsync(CancellationToken.None)) {
                _contexts.Remove(context);
            }
        }

        public async Task AddFileContextAsync(PythonFileContext context, CancellationToken cancellationToken) {
            ThrowIfDisposed();

            ContextState state;

            using (await _contextLock.WaitAsync(cancellationToken)) {
                if (!_contexts.TryGetValue(context, out state)) {
                    _contexts[context] = state = new ContextState(this, context, _disposing.Token);
                    context.Disposed += Context_Disposed;
                    context.DocumentsChanged += Context_DocumentsChanged;
                    context.SourceDocumentContentChanged += Context_SourceDocumentContentChanged;
                }
            }

            var docs = await context.GetDocumentsAsync(cancellationToken);

            foreach (var doc in docs) {
                AnalysisState item;
                if (!state.AnalysisStates.TryGetValue(doc.Moniker, out item)) {
                    item = new AnalysisState(doc, context);
                    state.AnalysisStates.Add(doc.Moniker, item);
                }
            }
        }

        private async void Context_DocumentsChanged(object sender, EventArgs e) {
            var context = (PythonFileContext)sender;
            await AddFileContextAsync(context, CancellationToken.None);
        }

        private async void Context_SourceDocumentContentChanged(object sender, SourceDocumentEventArgs e) {
            var context = (PythonFileContext)sender;
            var item = await GetAnalysisStateAsync(context, e.Document.Moniker, false, CancellationToken.None)
                .ConfigureAwait(false);

            using (await _contextLock.WaitAsync(CancellationToken.None)) {
                ContextState state;
                if (_contexts.TryGetValue(context, out state)) {
                    state.Enqueue(new DocumentChanged(item, e.Document));
                }
            }
        }

        internal async Task AddNotificationAsync(
            AnalysisState whenUpdated,
            AnalysisState reanalyze,
            CancellationToken cancellationToken
        ) {
            using (await _contextLock.WaitAsync(cancellationToken)) {
                ContextState state;
                HashSet<AnalysisState> states;
                if (_contexts.TryGetValue(whenUpdated.Context, out state)) {
                    var rou = state.ReanalyzeOnUpdate;
                    if (rou == null) {
                        state.ReanalyzeOnUpdate = rou = new Dictionary<string, HashSet<AnalysisState>>();
                        whenUpdated.Context.SourceDocumentAnalysisChanged += Context_SourceDocumentAnalysisChanged;
                    }
                    if (!rou.TryGetValue(whenUpdated.Document.Moniker, out states)) {
                        rou[whenUpdated.Document.Moniker] = states = new HashSet<AnalysisState>();
                    }
                    states.Add(reanalyze);
                }
            }
        }

        private async void Context_SourceDocumentAnalysisChanged(object sender, SourceDocumentEventArgs e) {
            var context = (PythonFileContext)sender;
            IEnumerable<AnalysisState> toUpdate = null;
            using (await _contextLock.WaitAsync(CancellationToken.None)) {
                ContextState state;
                HashSet<AnalysisState> states = null;
                if (_contexts.TryGetValue(context, out state) &&
                    (state.ReanalyzeOnUpdate?.TryGetValue(e.Document.Moniker, out states) ?? false)) {
                    toUpdate = states?.ToArray();
                }
            }

            if (toUpdate != null) {
                foreach (var state in toUpdate) {
                    await EnqueueAsync(state.Context, new UpdateRules(state), CancellationToken.None);
                }
            }
        }

        internal async Task<AnalysisState> GetAnalysisStateAsync(
            PythonFileContext context,
            string moniker,
            bool searchAllContexts,
            CancellationToken cancellationToken
        ) {
            AnalysisState item = null;
            using (await _contextLock.WaitAsync(cancellationToken)) {
                ContextState state = null;
                if (context != null) {
                    if (_contexts.TryGetValue(context, out state)) {
                        state.AnalysisStates.TryGetValue(moniker, out item);
                    }
                    if (item == null && !searchAllContexts) {
                        return null;
                    }
                }

                if (item == null) {
                    foreach (var c in _contexts.Values) {
                        if (!c.AnalysisStates.TryGetValue(moniker, out item)) {
                            continue;
                        }

                        state = c;
                        break;
                    }
                }

                if (item?.Version == 0 && state != null) {
                    state.Enqueue(new DocumentChanged(item, item.Document));
                }
                return item;
            }
        }

        public async Task<object> GetItemTokenAsync(
            PythonFileContext context,
            string moniker,
            bool searchAllContexts,
            CancellationToken cancellationToken
        ) {
            return await GetAnalysisStateAsync(context, moniker, searchAllContexts, cancellationToken);
        }

        public async Task WaitForUpdateAsync(
            PythonFileContext context,
            string moniker,
            CancellationToken cancellationToken
        ) {
            await WaitForUpdateAsync(
                await GetAnalysisStateAsync(context, moniker, true, cancellationToken),
                cancellationToken
            );
        }

        public async Task WaitForUpdateAsync(object token, CancellationToken cancellationToken) {
            var state = token as AnalysisState;
            if (state == null) {
                return;
            }
            await state.WaitForUpdateAsync(cancellationToken);
        }

        public async Task<Tokenization> GetTokenizationAsync(
            PythonFileContext context,
            string moniker,
            CancellationToken cancellationToken
        ) {
            return await GetTokenizationAsync(
                await GetAnalysisStateAsync(context, moniker, true, cancellationToken),
                cancellationToken
            );
        }

        public async Task<Tokenization> GetTokenizationAsync(object token, CancellationToken cancellationToken) {
            var state = token as AnalysisState;
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
            return await GetAstAsync(
                await GetAnalysisStateAsync(context, moniker, true, cancellationToken),
                cancellationToken
            );
        }

        public async Task<PythonAst> GetAstAsync(object token, CancellationToken cancellationToken) {
            var state = token as AnalysisState;
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

            using (await _contextLock.WaitAsync(cancellationToken)) {
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
            }
        }

        public async Task<IReadOnlyCollection<string>> GetModuleMembersAsync(
            PythonFileContext context,
            string moniker,
            string localName,
            CancellationToken cancellationToken
        ) {
            var state = await GetAnalysisStateAsync(context, moniker, true, cancellationToken);
            if (state == null) {
                return null;
            }

            var members = await state.GetVariablesAsync(cancellationToken);

            string prefix = null;
            if (!string.IsNullOrEmpty(localName)) {
                prefix = localName + ".";
                return members
                    .Where(n => n.StartsWith(prefix) && n.IndexOf('.', prefix.Length) < 0)
                    .Select(n => n.Substring(prefix.Length))
                    .ToArray();
            }
            return members
                .Where(n => n.IndexOf('.') < 0)
                .ToArray();
        }

        public async Task<IReadOnlyCollection<AnalysisValue>> GetModuleMemberTypesAsync(
            PythonFileContext context,
            string moniker,
            string name,
            CancellationToken cancellationToken
        ) {
            var state = await GetAnalysisStateAsync(context, moniker, true, cancellationToken);
            if (state == null) {
                return EmptyAnalysisValues;
            }

            return await state.GetTypesAsync(name, cancellationToken);
        }

        internal async Task EnqueueAsync(
            PythonFileContext context,
            QueueItem item,
            CancellationToken cancellationToken
        ) {
            using (await _contextLock.WaitAsync(cancellationToken)) {
                ContextState state;
                if (_contexts.TryGetValue(context, out state)) {
                    state.Enqueue(item);
                }
            }
        }

        sealed class ContextState : IDisposable {
            private readonly PathSet<AnalysisState> _analysisStates;


            private readonly Thread _thread;
            private readonly PythonLanguageService _analyzer;
            private readonly PythonFileContext _context;
            private readonly CancellationToken _cancel;
            private bool _threadStarted;
            private ExceptionDispatchInfo _edi;
            private TaskCompletionSource<TaskScheduler> _tasks;

            public ContextState(
                PythonLanguageService analyzer,
                PythonFileContext context,
                CancellationToken cancellationToken
            ) {
                _analysisStates = new PathSet<AnalysisState>(context.ContextRoot);
                _analyzer = analyzer;
                _context = context;
                _cancel = cancellationToken;
                _tasks = new TaskCompletionSource<TaskScheduler>();
                _thread = new Thread(Worker);

                var name = context.ContextRoot;
                if (!string.IsNullOrEmpty(name)) {
                    if (name.Length > 30) {
                        name = name.Substring(0, 7) + "..." + name.Substring(name.Length - 19);
                    }
                    _thread.Name = "Analyzer: " + name;
                } else {
                    _thread.Name = "Analyzer";
                }
            }

            public PathSet<AnalysisState> AnalysisStates => _analysisStates;

            public Dictionary<string, HashSet<AnalysisState>> ReanalyzeOnUpdate { get; set; }

            public void Dispose() {
                if (_threadStarted) {
                    _thread.Join(1000);
                }
                if (_edi != null) {
                    _edi.Throw();
                }
            }

            public void Enqueue(QueueItem item) {
                _edi?.Throw();

                if (!_threadStarted) {
                    _threadStarted = true;
                    _thread.Start(this);
                    _tasks.Task.Wait();
                }

                Task.Factory.StartNew(async () => {
                    await item.PerformAsync(_analyzer, _context, _cancel);
                }, CancellationToken.None, TaskCreationOptions.None, _tasks.Task.Result);
            }

            private static void Worker(object o) {
                var syncContext = new AnalysisSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(syncContext);
                var thread = (ContextState)o;

                var tasks = TaskScheduler.FromCurrentSynchronizationContext();
                thread._tasks.SetResult(tasks);
                try {
                    while (true) {
                        syncContext.RunNext(thread._cancel);
                    }
                } catch (OperationCanceledException) {
                } catch (Exception ex) when (!ex.IsCriticalException()) {
                    Debug.Fail(ex.ToString());
                    thread._edi = ExceptionDispatchInfo.Capture(ex);
                }
            }

            class AnalysisSynchronizationContext : SynchronizationContext {
                struct Item {
                    public SendOrPostCallback d;
                    public object state;
                    public ManualResetEventSlim complete;
                }

                private readonly ManualResetEventSlim _posted;
                private readonly Queue<Item> _queue;

                public AnalysisSynchronizationContext() {
                    _posted = new ManualResetEventSlim();
                    _queue = new Queue<Item>();
                }

                private AnalysisSynchronizationContext(AnalysisSynchronizationContext copy) {
                    _posted = copy._posted;
                    _queue = copy._queue;
                }

                public override SynchronizationContext CreateCopy() {
                    return new AnalysisSynchronizationContext(this);
                }

                public override void Post(SendOrPostCallback d, object state) {
                    lock (_queue) {
                        _queue.Enqueue(new Item { d = d, state = state });
                    }
                    _posted.Set();
                }

                public override void Send(SendOrPostCallback d, object state) {
                    using (var complete = new ManualResetEventSlim()) {
                        lock (_queue) {
                            _queue.Enqueue(new Item { d = d, state = state, complete = complete });
                        }
                        _posted.Set();
                        complete.Wait();
                    }
                }

                public void RunNext(CancellationToken cancellationToken) {
                    _posted.Wait(cancellationToken);
                    Item item;
                    lock (_queue) {
                        if (!_queue.Any()) {
                            return;
                        }
                        item = _queue.Dequeue();
                        if (!_queue.Any()) {
                            _posted.Reset();
                        }
                    }
                    try {
                        item.d(item.state);
                    } finally {
                        item.complete?.Set();
                    }
                }
            }
        }
    }
}
