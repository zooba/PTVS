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
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Analysis.Values;
using System.Threading;
using System.IO;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    class AnalysisState : IAnalysisState {
        private readonly PythonFileContext _context;
        private ISourceDocument _document;

        private Tokenization _tokenization;
        private PythonAst _ast;
        private IReadOnlyCollection<ErrorResult> _errors;

        private IReadOnlyDictionary<string, Variable> _variables;
        private IReadOnlyCollection<AnalysisRule> _rules;

        private TaskCompletionSource<object> _updated;
        private TaskCompletionSource<object> _upToDate;
        private long _version;

        internal AnalysisState(ISourceDocument document, PythonFileContext context) {
            _document = document;
            _context = context;
            _upToDate = new TaskCompletionSource<object>();
        }

        public PythonFileContext Context => _context;
        public ISourceDocument Document => _document;
        public long Version => _version;
        public LanguageFeatures Features => _ast?.Features ?? default(LanguageFeatures);

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

        private void NotifyUpdated() {
            Interlocked.Exchange(ref _updated, null)?.SetResult(null);
            _context.NotifyDocumentAnalysisChanged(_document);
            Interlocked.Increment(ref _version);

            if (Volatile.Read(ref _upToDate) == null) {
                Interlocked.CompareExchange(ref _upToDate, new TaskCompletionSource<object>(), null);
            }
        }

        internal Task WaitForUpdateAsync(CancellationToken cancellationToken) {
            var updated = Volatile.Read(ref _updated);
            if (updated == null) {
                var tcs = new TaskCompletionSource<object>();
                updated = Interlocked.CompareExchange(ref _updated, tcs, null) ?? tcs;
            }

            var cancelTcs = new TaskCompletionSource<object>();
            cancellationToken.Register(cancelTcs.SetCanceled);
            return Task.WhenAny(updated.Task, cancelTcs.Task);
        }

        internal void NotifyUpToDate() {
            Interlocked.Exchange(ref _upToDate, null)?.SetResult(null);
        }

        public Task WaitForUpToDateAsync(CancellationToken cancellationToken) {
            var upToDate = Volatile.Read(ref _upToDate);
            if (upToDate == null) {
                return Task.FromResult<object>(null);
            }

            var cancelTcs = new TaskCompletionSource<object>();
            cancellationToken.Register(cancelTcs.SetCanceled);
            return Task.WhenAny(upToDate.Task, cancelTcs.Task);
        }

        public async Task<Tokenization> GetTokenizationAsync(CancellationToken cancellationToken) {
            var tokenization = _tokenization;
            while (tokenization == null) {
                await WaitForUpdateAsync(cancellationToken);
                tokenization = _tokenization;
            }
            return tokenization;
        }

        public async Task<PythonAst> GetAstAsync(CancellationToken cancellationToken) {
            var ast = _ast;
            while (ast == null) {
                await WaitForUpdateAsync(cancellationToken);
                ast = _ast;
            }
            return ast;
        }

        internal IReadOnlyDictionary<string, Variable> GetVariables() {
            return _variables;
        }

        internal IEnumerable<AnalysisRule> GetRules() {
            return _rules;
        }

        internal async Task GetVariablesAndRules(
            Action<IReadOnlyDictionary<string, Variable>, IReadOnlyCollection<AnalysisRule>> action,
            CancellationToken cancellationToken
        ) {
            var variables = _variables;
            var rules = _rules;
            while (variables == null || rules == null) {
                await WaitForUpdateAsync(cancellationToken);
                variables = _variables;
                rules = _rules;
            }
            action?.Invoke(variables, rules);
        }

        public async Task<IReadOnlyCollection<string>> GetVariablesAsync(
            CancellationToken cancellationToken
        ) {
            var variables = _variables;
            var rules = _rules;
            while (variables == null || rules == null) {
                await WaitForUpdateAsync(cancellationToken);
                variables = _variables;
                rules = _rules;
            }
            return variables.Keys
                .Union(rules.SelectMany(r => r.GetVariableNames()))
                .ToArray();
        }

        public async Task<IReadOnlyCollection<AnalysisValue>> GetTypesAsync(
            string name,
            CancellationToken cancellationToken
        ) {
            var variables = _variables;
            var rules = _rules;
            while (variables == null || rules == null) {
                await WaitForUpdateAsync(cancellationToken);
                variables = _variables;
                rules = _rules;
            }

            if (!variables.ContainsKey(name)) {
                return null;
            }

            return variables[name].Types
                .Concat(rules.SelectMany(r => r.GetTypes(name)))
                .Where(t => t != AnalysisValue.Empty)
                .ToArray();
        }

        internal void Dump(TextWriter output) {
            output.WriteLine("Analysis Dump: {0}", _document.Moniker);
            output.WriteLine("  Version: {0}, Futures: {1}", Features.Version, Features.Future);
            output.WriteLine("  State Version: {0}", Version);
            if (_variables != null) {
                output.WriteLine("Variables");
                foreach (var v in _variables.OrderBy(kv => kv.Key)) {
                    output.WriteLine("  {0} = {1}", v.Key, v.Value.ToAnnotationString(this));
                }
                output.WriteLine();
            }
            if (_rules != null) {
                output.WriteLine("Rules");
                foreach (var r in _rules) {
                    r.Dump(output, this, "  ");
                }
                output.WriteLine();
            }
            output.WriteLine("End of dump ({0} variables, {1} rules)", _variables?.Count ?? 0, _rules?.Count ?? 0);
            output.WriteLine();
        }
    }
}
