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
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis {
    class RuleResults : IEnumerable<KeyValuePair<string, IAnalysisSet>> {
        private readonly Dictionary<string, AnalysisSet> _dict;
        private readonly List<Listener> _listeners;

        public RuleResults() {
            _dict = new Dictionary<string, AnalysisSet>();
            _listeners = new List<Listener>();
        }

        public RuleResults(IEnumerable<string> keys) {
            _dict = keys.MaybeEnumerate().ToDictionary(k => k, k => new AnalysisSet());
            _listeners = new List<Listener>();
        }

        private RuleResults(Dictionary<string, AnalysisSet> dict) {
            _dict = dict;
            _listeners = new List<Listener>();
        }

        public RuleResults Clone() {
            return new RuleResults(new Dictionary<string, AnalysisSet>(_dict));
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

        public bool AddTypes(string key, IAnalysisSet types) {
            AnalysisSet set;
            if (!_dict.TryGetValue(key, out set)) {
                _dict[key] = set = new AnalysisSet();
            }
            var beforeVersion = set.Version;
            set.AddRange(types);
            if (set.Version == beforeVersion) {
                return false;
            }

            foreach (var listener in _listeners) {
                listener._writes?.Add(key);
            }
            return true;
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

        public IEnumerator<KeyValuePair<string, IAnalysisSet>> GetEnumerator() {
            return _dict.Select(kv => new KeyValuePair<string, IAnalysisSet>(kv.Key, kv.Value)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

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
    }
}
