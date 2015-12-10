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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Values;

namespace Microsoft.PythonTools.Analysis {
    abstract class AnalysisRule {
        private Dictionary<string, IAnalysisSet> _results;
        private object _targets;

        private static readonly IReadOnlyCollection<string> EmptyNames = new string[0];

        protected AnalysisRule(string target) {
            _targets = target;
        }

        protected AnalysisRule(IEnumerable<string> targets) {
            var targetsArray = targets.ToArray();
            if (targetsArray.Length == 0) {
                _targets = null;
            } else if (targetsArray.Length == 1) {
                _targets = targetsArray[0];
            } else {
                _targets = targetsArray;
            }
        }

        protected IEnumerable<string> Targets {
            get {
                var asEnum = _targets as IEnumerable<string>;
                if (asEnum != null) {
                    return asEnum;
                }
                var asStr = _targets as string;
                if (asStr != null) {
                    return Enumerable.Repeat(asStr, 1);
                }
                return Enumerable.Empty<string>();
            }
        }

        protected bool AreSame(
            string target,
            IReadOnlyDictionary<string, IAnalysisSet> priorResults,
            IAnalysisSet newResults
        ) {
            if (priorResults == null) {
                return newResults == null;
            } else if (newResults == null) {
                return false;
            }
            IAnalysisSet oldResults;
            if (!priorResults.TryGetValue(target, out oldResults)) {
                return false;
            }
            return newResults.SetEquals(oldResults);
        }

        public IReadOnlyCollection<string> GetVariableNames() {
            var results = Volatile.Read(ref _results);
            return results?.Keys ?? EmptyNames;
        }

        public IAnalysisSet GetTypes(string name) {
            var results = Volatile.Read(ref _results);
            if (results == null) {
                return AnalysisSet.Empty;
            }
            IAnalysisSet v;
            return results.TryGetValue(name, out v) ? v : AnalysisSet.Empty;
        }

        public async Task<bool> ApplyAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            CancellationToken cancellationToken
        ) {
            var oldResults = Volatile.Read(ref _results);
            var newResults = await ApplyWorkerAsync(analyzer, state, oldResults, cancellationToken);
            if (newResults != null) {
                Interlocked.CompareExchange(ref _results, newResults, oldResults);
                return true;
            }
            return false;
        }

        protected abstract Task<Dictionary<string, IAnalysisSet>> ApplyWorkerAsync(
            PythonLanguageService analyzer, 
            AnalysisState state,
            IReadOnlyDictionary<string, IAnalysisSet> priorResults,
            CancellationToken cancellationToken
        );

        internal virtual async Task Dump(
            TextWriter output,
            IAnalysisState state,
            string indent,
            CancellationToken cancellationToken
        ) {
            output.WriteLine("{0}{1}", indent, this);
            if (_results != null) {
                foreach (var v in _results.OrderBy(kv => kv.Key)) {
                    output.WriteLine(
                        "{0}  {1}: {2}",
                        indent,
                        v.Key,
                        await v.Value.ToAnnotationAsync(cancellationToken)
                    );
                }
            }
        }
    }
}
