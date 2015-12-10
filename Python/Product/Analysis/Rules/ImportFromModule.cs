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

namespace Microsoft.PythonTools.Analysis.Rules {
    class ImportFromModule : AnalysisRule {
        private readonly string _moduleMoniker;
        private readonly string _importName;

        private long _lastVersion;

        public ImportFromModule(string moduleMoniker, string importName, string targetName)
            : base(targetName) {
            _moduleMoniker = moduleMoniker;
            _importName = importName;
        }

        protected override async Task<Dictionary<string, IAnalysisSet>> ApplyWorkerAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            IReadOnlyDictionary<string, IAnalysisSet> priorResults,
            CancellationToken cancellationToken
        ) {
            if (string.IsNullOrEmpty(_moduleMoniker)) {
                return null;
            }

            var importState = analyzer.GetAnalysisState(state.Context, _moduleMoniker, false) as AnalysisState;
            if (importState != null) {
                await analyzer.AddNotificationAsync(importState, state, cancellationToken);
                return await GetImportsAsync(analyzer, state, importState, priorResults, cancellationToken);
            }

            importState = analyzer.GetAnalysisState(null, _moduleMoniker, true) as AnalysisState;
            if (importState != null) {
                await analyzer.AddNotificationAsync(importState, state, cancellationToken);
                return await GetCrossContextImportsAsync(analyzer, state, importState, priorResults, cancellationToken);
            }

            return null;
        }

        private async Task<Dictionary<string, IAnalysisSet>> GetImportsAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            AnalysisState importState,
            IReadOnlyDictionary<string, IAnalysisSet> priorResults,
            CancellationToken cancellationToken
        ) {
            if (importState.Version <= _lastVersion) {
                return null;
            }

            var results = new Dictionary<string, IAnalysisSet>();
            var n = string.IsNullOrEmpty(_importName) ? ModuleValue.VariableName : _importName;
            if (n == "*") {
                IReadOnlyDictionary<string, Variable> vars = null;
                await importState.GetVariablesAndRulesAsync((v, _) => { vars = v; }, cancellationToken);
                foreach (var v in vars ?? Enumerable.Empty<KeyValuePair<string, Variable>>()) {
                    results[v.Key] = v.Value.Types;
                }
            } else {
                foreach (var target in Targets) {
                    results[target] = await importState.GetTypesAsync(n, cancellationToken);
                }
            }
            _lastVersion = importState.Version;
            return results;
        }

        private async Task<Dictionary<string, IAnalysisSet>> GetCrossContextImportsAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            AnalysisState importState,
            IReadOnlyDictionary<string, IAnalysisSet> priorResults,
            CancellationToken cancellationToken
        ) {
            if (importState.Version <= _lastVersion) {
                return null;
            }

            var results = new Dictionary<string, IAnalysisSet>();
            var n = string.IsNullOrEmpty(_importName) ? ModuleValue.VariableName : _importName;
            if (n == "*") {
                IReadOnlyDictionary<string, Variable> vars = null;
                await importState.GetVariablesAndRulesAsync((v, _) => { vars = v; }, cancellationToken);
                foreach (var v in vars ?? Enumerable.Empty<KeyValuePair<string, Variable>>()) {
                    results[v.Key] = v.Value.Types;
                }
            } else {
                foreach (var target in Targets) {
                    results[target] = await importState.GetTypesAsync(n, cancellationToken);
                }
            }
            _lastVersion = importState.Version;
            return results;
        }
    }
}
