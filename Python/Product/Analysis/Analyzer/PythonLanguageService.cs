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
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public sealed class PythonLanguageService : IDisposable {
        private readonly InterpreterConfiguration _config;
        private readonly CancellationTokenSource _disposing;
        private int _users;

        private readonly SemaphoreSlim _contextLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<PythonFileContext, PathSet<AnalysisState>> _contexts;

        private readonly SemaphoreSlim _searchPathsLock = new SemaphoreSlim(1, 1);
        private readonly List<KeyValuePair<string, string[]>> _searchPaths;

        private readonly QueueThread _updateTreeThread;
        private readonly QueueThread _updateMemberListThread;

        private static readonly Regex ImportNameRegex = new Regex(
            @"^([\w_][\w\d_]+)(\.py[wcd]?)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)
        );

        public PythonLanguageService(InterpreterConfiguration config) {
            _users = 1;

            _disposing = new CancellationTokenSource();
            _config = config;

            _contexts = new Dictionary<PythonFileContext, PathSet<AnalysisState>>();
            _searchPaths = new List<KeyValuePair<string, string[]>>();
            _searchPaths.AddRange(config.SysPath.Select(p => new KeyValuePair<string, string[]>(p, null)));

            _updateTreeThread = new QueueThread(this, _disposing.Token);
            _updateMemberListThread = new QueueThread(this, _disposing.Token);
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

            _updateTreeThread.Dispose();
            _updateMemberListThread.Dispose();
            // TODO: Clean up service
        }

        #endregion

        public InterpreterConfiguration Configuration {
            get { return _config; }
        }

        internal void Enqueue(DocumentChanged item) {
            _updateTreeThread.Enqueue(item);
        }

        internal void Enqueue(UpdateTree item) {
            _updateTreeThread.Enqueue(item);
        }

        internal void Enqueue(UpdateMemberList item) {
            _updateMemberListThread.Enqueue(item);
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

                    foreach (var files in _contexts.Values) {
                        AnalysisState value;
                        // TODO: Check whether .py file precedes /__init__.py
                        if (files.TryFindValueByParts(kv.Key, trimmedFileParts, out value) ||
                            files.TryFindValueByParts(kv.Key, trimmedInitParts, out value)) {
                            return value.Document.Get().Moniker;
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

            PathSet<AnalysisState> states;

            await _contextLock.WaitAsync(cancellationToken);
            try {
                if (!_contexts.TryGetValue(context, out states)) {
                    _contexts[context] = states = new PathSet<AnalysisState>(context.ContextRoot);
                    context.Disposed += Context_Disposed;
                }
            } finally {
                _contextLock.Release();
            }

            var docs = await context.GetDocumentsAsync(cancellationToken);

            foreach (var doc in docs) {
                AnalysisState state;
                if (!states.TryGetValue(doc.Moniker, out state)) {
                    state = new AnalysisState(doc, context);
                    states.Add(doc.Moniker, state);
                }
                Enqueue(new DocumentChanged(state, doc));
            }
        }

        private async Task<AnalysisState> GetAnalysisStateAsync(
            PythonFileContext context,
            string moniker,
            CancellationToken cancellationToken
        ) {
            PathSet<AnalysisState> states;
            await _contextLock.WaitAsync(cancellationToken);
            try {
                AnalysisState state;
                if (context == null) {
                    foreach (var c in _contexts.Values) {
                        if (c.TryGetValue(moniker, out state)) {
                            return state;
                        }
                    }
                    return null;
                }

                if (!_contexts.TryGetValue(context, out states)) {
                    return null;
                }

                if (!states.TryGetValue(moniker, out state)) {
                    return null;
                }
                return state;
            } finally {
                _contextLock.Release();
            }
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
            return await state.Tree.GetAsync();
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

                    foreach (var kv in _contexts) {
                        foreach (var c in kv.Value.GetChildren(searchPath.Key, parts.Skip(skipParts))) {
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

        public async Task<IReadOnlyDictionary<string, PythonMemberType>> GetModuleMembersAsync(
            PythonFileContext context,
            string moniker,
            string localName,
            CancellationToken cancellationToken
        ) {
            var state = await GetAnalysisStateAsync(context, moniker, cancellationToken);
            if (state == null) {
                return null;
            }

            var members = await state.MemberList.GetAsync();

            string prefix = null;
            if (!string.IsNullOrEmpty(localName)) {
                prefix = localName + ".";
            }
            return new DictionaryPrefixWrapper<PythonMemberType>(members, prefix, new[] { '.' });
        }

        internal class AnalysisState {
            public readonly PythonFileContext Context;
            public readonly WithVersion<ISourceDocument> Document;

            public readonly WithVersion<PythonAst> Tree;

            public readonly WithVersion<IReadOnlyDictionary<string, PythonMemberType>> MemberList;

            public readonly WithVersion<object> Analysis;

            public AnalysisState(ISourceDocument document, PythonFileContext context) {
                Document = new WithVersion<ISourceDocument>(document);
                Tree = new WithVersion<PythonAst>();
                MemberList = new WithVersion<IReadOnlyDictionary<string, PythonMemberType>>();
                Analysis = new WithVersion<object>();

                Context = context;
            }
        }

        sealed class QueueThread : IDisposable {
            private readonly Queue<QueueItem> _queue;
            private readonly Thread _thread;
            private readonly ManualResetEventSlim _queueChanged;
            private readonly PythonLanguageService _analyzer;
            private readonly CancellationToken _cancel;
            private ExceptionDispatchInfo _edi;

            public QueueThread(PythonLanguageService analyzer, CancellationToken cancellationToken) {
                _queue = new Queue<QueueItem>();
                _queueChanged = new ManualResetEventSlim();
                _analyzer = analyzer;
                _cancel = cancellationToken;
                _thread = new Thread(Worker);
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
                lock (_queue) {
                    _queue.Enqueue(item);
                    _queueChanged.Set();
                }
            }

            private static void Worker(object o) {
                try {
                    ((QueueThread)o).QueueWorker();
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    ((QueueThread)o)._edi = ExceptionDispatchInfo.Capture(ex);
                }
            }

            private void QueueWorker() {
                while (true) {
                    try {
                        _queueChanged.Wait(_cancel);
                    } catch (ObjectDisposedException) {
                        throw new OperationCanceledException();
                    }

                    if (_cancel.IsCancellationRequested) {
                        break;
                    }

                    QueueItem item;
                    while (true) {
                        lock (_queue) {
                            if (!_queue.Any()) {
                                try {
                                    _queueChanged.Reset();
                                } catch (ObjectDisposedException) {
                                    throw new OperationCanceledException();
                                }
                                break;
                            }
                            item = _queue.Dequeue();
                        }
                        item.PerformAsync(_analyzer, _cancel).GetAwaiter().GetResult();
                    }
                }
            }
        }

    }
}
