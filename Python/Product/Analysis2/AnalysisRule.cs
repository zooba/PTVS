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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Values;

namespace Microsoft.PythonTools.Analysis {
    abstract class AnalysisRule {
        private Dictionary<string, IReadOnlyCollection<AnalysisValue>> _results;


        public IReadOnlyCollection<string> GetVariableNames() {
            var results = Volatile.Read(ref _results);
            return results.Keys;
        }

        public IEnumerable<AnalysisValue> GetTypes(string name) {
            var results = Volatile.Read(ref _results);
            if (results == null) {
                return PythonLanguageService.EmptyAnalysisValues;
            }
            IReadOnlyCollection<AnalysisValue> v;
            return results.TryGetValue(name, out v) ? v : PythonLanguageService.EmptyAnalysisValues;
        }

        public async Task ApplyAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            CancellationToken cancellationToken
        ) {
            var oldResults = Volatile.Read(ref _results);
            var newResults = await ApplyWorkerAsync(analyzer, state, oldResults, cancellationToken);
            if (newResults != null) {
                Interlocked.CompareExchange(ref _results, newResults, oldResults);
            }
        }

        protected abstract Task<Dictionary<string, IReadOnlyCollection<AnalysisValue>>> ApplyWorkerAsync(
            PythonLanguageService analyzer, 
            AnalysisState state,
            IReadOnlyDictionary<string, IReadOnlyCollection<AnalysisValue>> priorResults,
            CancellationToken cancellationToken
        );
    }
}
