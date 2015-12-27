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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis {
    class RuleResults : IAnalysisState, IEnumerable<KeyValuePair<string, IAnalysisSet>> {
        private readonly IAnalysisState _state;
        private readonly Dictionary<string, AnalysisSet> _dict;
        private readonly List<Listener> _listeners;

        public RuleResults(IAnalysisState state) {
            _state = state;
            _dict = new Dictionary<string, AnalysisSet>();
            _listeners = new List<Listener>();
        }

        public RuleResults(IAnalysisState state, IEnumerable<string> keys) {
            _state = state;
            _dict = keys.MaybeEnumerate().ToDictionary(k => k, k => new AnalysisSet());
            _listeners = new List<Listener>();
        }

        private RuleResults(IAnalysisState state, Dictionary<string, AnalysisSet> dict) {
            _state = state;
            _dict = dict;
            _listeners = new List<Listener>();
        }

        public RuleResults Clone() {
            return new RuleResults(_state, new Dictionary<string, AnalysisSet>(_dict));
        }

        internal async Task Dump(
            TextWriter output,
            string indent,
            CancellationToken cancellationToken
        ) {
            foreach (var v in _dict.OrderBy(kv => kv.Key)) {
                output.WriteLine(
                    "{0}  {1}: {2}",
                    indent,
                    v.Key,
                    await v.Value.ToAnnotationAsync(cancellationToken)
                );
            }
        }

        public IReadOnlyCollection<string> Keys => _dict.Keys.ToArray();
        public IAnalysisSet Types => _dict.Values.SelectMany().ToSet();

        public IReadOnlyDictionary<string, IAnalysisSet> Freeze() {
            var res = new Dictionary<string, IAnalysisSet>();
            foreach (var kv in _dict) {
                res[kv.Key] = kv.Value.Trim();
            }
            return res;
        }

        public Task AddTypesAsync(string key, IAnalysisSet types, CancellationToken cancellationToken) {
            AnalysisSet set;
            if (!_dict.TryGetValue(key, out set)) {
                _dict[key] = set = new AnalysisSet();
            }
            var beforeVersion = set.Version;
            set.AddRange(types);
            if (set.Version != beforeVersion) {
                foreach (var listener in _listeners) {
                    listener._writes?.Add(key);
                }
            }
            return Task.FromResult<object>(null);
        }

        public async Task<IAnalysisSet> GetVariableTypes(
            IAnalysisState caller,
            VariableKey key,
            CancellationToken cancellationToken
        ) {
            var globalSet = key.GetTypes(caller) ?? await key.GetTypesAsync(cancellationToken);
            return globalSet.Concat(GetTypes(key.Key)).ToSet();
        }

        public IAnalysisSet GetTypes(string key) {
            return TryGetTypes(key) ?? AnalysisSet.Empty;
        }

        public IAnalysisSet TryGetTypes(string key) {
            foreach (var listener in _listeners) {
                listener._reads?.Add(key);
            }

            AnalysisSet localSet;
            if (!_dict.TryGetValue(key, out localSet)) {
                return null;
            }

            return localSet ?? AnalysisSet.Empty;
        }

        public IDisposable Track(HashSet<string> reads, HashSet<string> writes) {
            return new Listener(_listeners, reads, writes);
        }

        public IAssignable AsAssignable(IEnumerable<string> names) {
            return new AssignTarget {
                Results = this,
                Keys = names.Select(n => new VariableKey(_state, n)).ToArray()
            };
        }

        public IEnumerator<KeyValuePair<string, IAnalysisSet>> GetEnumerator() {
            return _dict.Select(kv => new KeyValuePair<string, IAnalysisSet>(kv.Key, kv.Value)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        #region IAnalysisState implementation

        public PythonLanguageService Analyzer => _state.Analyzer;
        public PythonFileContext Context => _state.Context;
        public ISourceDocument Document => _state.Document;
        public long Version => _state.Version;
        public LanguageFeatures Features => _state.Features;

        public Task DumpAsync(TextWriter output, CancellationToken cancellationToken) {
            return _state.DumpAsync(output, cancellationToken);
        }

        public Task WaitForUpToDateAsync(CancellationToken cancellationToken) {
            return _state.WaitForUpToDateAsync(cancellationToken);
        }

        public Tokenization TryGetTokenization() {
            return _state.TryGetTokenization();
        }

        public Task<Tokenization> GetTokenizationAsync(CancellationToken cancellationToken) {
            return _state.GetTokenizationAsync(cancellationToken);
        }

        public Task<PythonAst> GetAstAsync(CancellationToken cancellationToken) {
            return _state.GetAstAsync(cancellationToken);
        }

        public Task<IReadOnlyCollection<string>> GetVariablesAsync(CancellationToken cancellationToken) {
            return _state.GetVariablesAsync(cancellationToken);
        }

        public Task<IAnalysisSet> GetTypesAsync(string name, CancellationToken cancellationToken) {
            return _state.GetTypesAsync(name, cancellationToken);
        }

        public Task<string> GetFullNameAsync(string name, SourceLocation location, CancellationToken cancellationToken) {
            return _state.GetFullNameAsync(name, location, cancellationToken);
        }

        public Task<bool> ReportErrorAsync(string code, string text, SourceLocation location, CancellationToken cancellationToken) {
            return _state.ReportErrorAsync(code, text, location, cancellationToken);
        }

        #endregion

        sealed class Listener : IDisposable {
            private readonly List<Listener> _owner;
            internal readonly HashSet<string> _reads, _writes;

            internal Listener(List<Listener> owner, HashSet<string> reads, HashSet<string> writes) {
                _owner = owner;
                _owner.Add(this);
                _reads = reads;
                _writes = writes;
            }

            public void Dispose() {
                _owner.Remove(this);
            }
        }

        sealed class AssignTarget : IAssignable {
            public RuleResults Results { get; set; }
            public IEnumerable<VariableKey> Keys { get; set; }

            public Task AddTypeAsync(VariableKey key, IAnalysisSet values, CancellationToken cancellationToken) {
                return Results.AddTypesAsync(key.Key, values, cancellationToken);
            }

            public async Task AddTypesAsync(IAnalysisSet values, CancellationToken cancellationToken) {
                foreach (var key in Keys) {
                    await Results.AddTypesAsync(key.Key, values, cancellationToken);
                }
            }

            public IAssignable WithSuffix(string suffix) {
                return new AssignTarget {
                    Results = Results,
                    Keys = Keys.Select(k => k + suffix).ToArray()
                };
            }
        }
    }
}
