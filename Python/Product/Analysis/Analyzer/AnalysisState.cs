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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Infrastructure;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    class AnalysisState : IAnalysisState {
        private readonly PythonLanguageService _analyzer;
        private readonly IAnalysisThread _thread;
        private readonly PythonFileContext _context;
        private ISourceDocument _document;

        private Tokenization _tokenization;
        private PythonAst _ast;
        private IReadOnlyCollection<ErrorResult> _errors;

        private IReadOnlyDictionary<string, Variable> _variables;
        private IReadOnlyCollection<AnalysisRule> _rules;
        private RuleResults _ruleResults, _pendingRuleResults;

        private TaskCompletionSource<object> _updated;
        private TaskCompletionSource<object> _upToDate;
        private ExceptionDispatchInfo _exception;
        private long _version;

        private readonly List<List<IFormattable>> _trace;
        private int _traceIndex;
        private const int TraceChunk = 100;

        internal AnalysisState(
            PythonLanguageService analyzer,
            IAnalysisThread thread,
            ISourceDocument document,
            PythonFileContext context
        ) {
            _analyzer = analyzer;
            _thread = thread;
            _document = document;
            _context = context;
            _upToDate = new TaskCompletionSource<object>();
            _trace = new List<List<IFormattable>>();
            _pendingRuleResults = new RuleResults();
        }

        public PythonLanguageService Analyzer => _analyzer;
        public PythonFileContext Context => _context;
        public ISourceDocument Document => _document;
        public long Version => _version;
        public LanguageFeatures Features => _ast?.Features ?? default(LanguageFeatures);

        #region Tracing

        internal int TraceCapacity {
            get {
                return _trace.Count * TraceChunk;
            }
            set {
                if (value == 0) {
                    _trace.Clear();
                    _traceIndex = -1;
                    return;
                }
                int desired = Math.Max((value - 1) / TraceChunk + 1, 1);
                while (_trace.Count > desired && _traceIndex < _trace.Count) {
                    _trace.RemoveAt(_trace.Count);
                }
                while (_trace.Count > desired) {
                    _trace.RemoveAt(0);
                    _traceIndex -= 1;
                }
                while (_trace.Count < desired) {
                    _trace.Add(new List<IFormattable>(TraceChunk));
                }
            }
        }

        public Task TraceAsync(IFormattable message) {
            if (_thread == null || _thread.IsCurrent) {
                Trace(message);
                return Task.FromResult<object>(null);
            }
            return _thread.Post(async () => { Trace(message); });
        }

        public void Trace(IFormattable message) {
            Debug.Assert(_thread == null || _thread.IsCurrent, "Must not be called from off analysis thread");

            if (_traceIndex < 0 || _traceIndex >= _trace.Count) {
                return;
            }
            var trace = _trace[_traceIndex];
            while (trace.Count == TraceChunk) {
                _traceIndex += 1;
                if (_traceIndex > _trace.Count) {
                    _traceIndex = 0;
                }
                _trace[_traceIndex] = trace = new List<IFormattable>(TraceChunk);
            }
            trace.Add(message);
        }

        public async Task DumpTraceAsync(TextWriter output, CancellationToken cancellationToken) {
            await InvokeAsync(async () => {
                for (int i = _traceIndex + 1; i < _trace.Count; ++i) {
                    foreach (var m in _trace[i]) {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (m != null) {
                            output.WriteLine(m.ToString());
                        }
                    }
                }
                for (int i = 0; i < _trace.Count && i <= _traceIndex; ++i) {
                    foreach (var m in _trace[i]) {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (m != null) {
                            output.WriteLine(m.ToString());
                        }
                    }
                }
                _trace.Clear();
                _traceIndex = 0;
            });
        }

        #endregion

        internal void SetDocument(ISourceDocument document) {
            _document = document;
            NotifyUpdated();
        }

        internal void SetTokenization(Tokenization tokenization) {
            _tokenization = tokenization;
            NotifyUpdated();
        }

        internal void SetAst(PythonAst ast, IReadOnlyList<ErrorResult> errors) {
            _ast = ast;
            _errors = errors;
            NotifyUpdated();
        }

        internal void SetVariablesAndRules(
            IReadOnlyDictionary<string, Variable> variables,
            IReadOnlyCollection<AnalysisRule> rules
        ) {
            _variables = variables;
            _rules = rules;
            NotifyUpdated();
        }

        internal RuleResults BeginSetRuleResults() {
            return _pendingRuleResults = (_ruleResults?.Clone()) ?? new RuleResults();
        }

        internal void EndSetRuleResults() {
            _ruleResults = _pendingRuleResults;
            NotifyUpdated();
        }

        private void NotifyUpdated() {
            if (Volatile.Read(ref _upToDate) == null) {
                Interlocked.CompareExchange(ref _upToDate, new TaskCompletionSource<object>(), null);
            }

            Interlocked.Exchange(ref _updated, null)?.SetResult(null);
            _context?.NotifyDocumentAnalysisChanged(_document);
            Interlocked.Increment(ref _version);
        }

        internal async Task WaitForUpdateAsync(CancellationToken cancellationToken) {
            _exception?.Throw();

            var updated = Volatile.Read(ref _updated);
            if (updated == null) {
                var tcs = new TaskCompletionSource<object>();
                updated = Interlocked.CompareExchange(ref _updated, tcs, null) ?? tcs;
            }

            if (cancellationToken.CanBeCanceled) {
                // Don't cancel updated, since others may be listening for it.
                // So create a new task and abort that one.
                var cancelTcs = new TaskCompletionSource<object>();
                updated.Task.ContinueWith(t => {
                    try {
                        cancelTcs.TrySetResult(null);
                    } catch (ObjectDisposedException) {
                    } catch (NullReferenceException) {
                    }
                }).DoNotWait();
                using (cancellationToken.Register(() => cancelTcs.TrySetCanceled())) {
                    await cancelTcs.Task;
                }
                cancelTcs = null;
            } else {
                await updated.Task;
            }
        }

        internal void NotifyUpToDate() {
            Trace($"{_document.Moniker} up to date at version {Version}");
            Interlocked.Exchange(ref _upToDate, null)?.SetResult(null);
        }

        public async Task WaitForUpToDateAsync(CancellationToken cancellationToken) {
            _exception?.Throw();

            var upToDate = Volatile.Read(ref _upToDate);
            if (upToDate == null) {
                return;
            }

            var cancelTcs = new TaskCompletionSource<object>();
            upToDate.Task.ContinueWith(t => {
                var c = cancelTcs;
                if (c != null) {
                    if (t.Exception != null) {
                        c.TrySetException(t.Exception);
                    } else {
                        c.TrySetResult(null);
                    }
                }
            }).DoNotWait();
            using (cancellationToken.Register(() => cancelTcs.TrySetCanceled())) {
                await cancelTcs.Task;
                cancelTcs = null;
            }
        }

        internal void NotifyError(ExceptionDispatchInfo ex) {
            _exception = ex;

            var upToDate = Volatile.Read(ref _upToDate);
            upToDate?.TrySetException(ex.SourceException);
            var updated = Volatile.Read(ref _updated);
            updated?.TrySetException(ex.SourceException);
        }

        private Task InvokeAsync(Func<Task> func) {
            if (_thread != null && !_thread.IsCurrent) {
                return _thread.Post(func);
            }
            return func();
        }

        private Task<T> InvokeAsync<T>(Func<Task<T>> func) {
            if (_thread != null && !_thread.IsCurrent) {
                return _thread.Post(func);
            }
            return func();
        }

        public Task<Tokenization> GetTokenizationAsync(CancellationToken cancellationToken) {
            return InvokeAsync(async () => {
                var t = _tokenization;
                while (t == null) {
                    await WaitForUpdateAsync(cancellationToken);
                    t = _tokenization;
                }
                return t;
            });
        }

        public Tokenization TryGetTokenization() {
            return _tokenization;
        }

        public Task<PythonAst> GetAstAsync(CancellationToken cancellationToken) {
            return InvokeAsync(async () => {
                var a = _ast;
                while (a == null) {
                    await WaitForUpdateAsync(cancellationToken);
                    a = _ast;
                }
                return a;
            });
        }

        internal IReadOnlyDictionary<string, Variable> GetVariables() {
            return _variables;
        }

        internal IEnumerable<AnalysisRule> GetRules() {
            return _rules;
        }

        internal RuleResults GetPendingRuleResults() {
            return _pendingRuleResults;
        }

        internal Task GetVariablesAndRulesAsync(
            Action<IReadOnlyDictionary<string, Variable>, IReadOnlyCollection<AnalysisRule>> action,
            CancellationToken cancellationToken
        ) {
            return InvokeAsync(async () => {
                var v = _variables;
                var r = _rules;
                while (v == null || r == null) {
                    await WaitForUpdateAsync(cancellationToken);
                    v = _variables;
                    r = _rules;
                }
                action?.Invoke(v, r);
            });
        }

        public async Task<IReadOnlyCollection<string>> GetVariablesAsync(
            CancellationToken cancellationToken
        ) {
            IReadOnlyCollection<string> result = null;
            await GetVariablesAndRulesAsync((variables, rules) => {
                result = variables.Keys.ToArray();
            }, cancellationToken);
            return result ?? new string[0];
        }

        public Task<string> GetFullNameAsync(
            string name,
            SourceLocation location,
            CancellationToken cancellationToken
        ) {
            return InvokeAsync(async () => {
                var ast = _ast;
                var variables = _variables;
                while (ast == null || variables == null) {
                    await WaitForUpdateAsync(cancellationToken);
                    ast = _ast;
                    variables = _variables;
                }

                return FindScopeFromLocationWalker
                    .FindNames(ast, location, name)
                    .MaybeEnumerate()
                    .FirstOrDefault(variables.ContainsKey) ?? name;
            });
        }

        public Task<Dictionary<string, IAnalysisSet>> GetAllTypesAsync(CancellationToken cancellationToken) {
            return InvokeAsync(async () => {
                var v = _variables;
                var r = _ruleResults;
                while (v == null) {
                    await WaitForUpdateAsync(cancellationToken);
                    v = _variables;
                    r = _ruleResults;
                }
                var result = new Dictionary<string, IAnalysisSet>();
                foreach (var kv in v) {
                    if (r != null) {
                        result[kv.Key] = kv.Value.Types.Union(r.GetTypes(kv.Key));
                    } else {
                        result[kv.Key] = kv.Value.Types;
                    }
                }
                return result;
            });
        }

        public Task<IAnalysisSet> GetTypesAsync(
            string name,
            CancellationToken cancellationToken
        ) {
            return InvokeAsync((Func<Task<IAnalysisSet>>)(async () => {
                var v = _variables;
                var r = _ruleResults;
                while (v == null) {
                    await WaitForUpdateAsync(cancellationToken);
                    v = _variables;
                    r = _ruleResults;
                }
                Variable variable;
                if (GetVariables().TryGetValue((string)name, out variable)) {
                    if (r != null) {
                        return variable.Types.Union(r.GetTypes(name));
                    } else {
                        return variable.Types;
                    }
                }
                return AnalysisSet.Empty;
            }));
        }

        private async Task DumpOnThreadAsync(TextWriter output, CancellationToken cancellationToken) {
            output.WriteLine("Analysis Dump: {0}", _document.Moniker);
            output.WriteLine("  Version: {0}, Futures: {1}", Features.Version, Features.Future);
            output.WriteLine("  State Version: {0}", Version);
            if (_variables != null) {
                output.WriteLine("Variables");
                foreach (var v in _variables.OrderBy(kv => kv.Key)) {
                    output.WriteLine(
                        "  {0} = {1}",
                        v.Key,
                        await v.Value.ToAnnotationStringAsync(cancellationToken)
                    );
                }
                output.WriteLine();
            }
            if (_ruleResults != null) {
                output.WriteLine("Rule Results");
                await _ruleResults.Dump(output, "  ", cancellationToken);
                output.WriteLine();
            }
            if (_rules != null) {
                output.WriteLine("Rules");
                foreach (var r in _rules) {
                    output.WriteLine("{0}{1}", "  ", r);
                }
                output.WriteLine();
            }
            output.WriteLine("End of dump ({0} variables, {1} rules)", _variables?.Count ?? 0, _rules?.Count ?? 0);
            output.WriteLine();
        }

        public Task DumpAsync(TextWriter output, CancellationToken cancellationToken) {
            if (_thread == null) {
                return DumpOnThreadAsync(output, cancellationToken);
            }
            return _thread.Post(() => DumpOnThreadAsync(output, cancellationToken));
        }

        public async Task<bool> ReportErrorAsync(
            string code,
            string text,
            SourceLocation location,
            CancellationToken cancellationToken
        ) {
            return false;
        }
    }
}
