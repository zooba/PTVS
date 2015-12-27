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
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis.Rules {
    class ImportFromModule : AnalysisRule {
        private readonly string _moduleMoniker;
        private readonly string _importName;

        private long _lastVersion;

        public ImportFromModule(AnalysisState state, string moduleMoniker, string importName, string targetName)
            : base(state, targetName) {
            _moduleMoniker = moduleMoniker;
            _importName = importName;
        }

        protected override async Task ApplyWorkerAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            RuleResults results,
            CancellationToken cancellationToken
        ) {
            if (string.IsNullOrEmpty(_moduleMoniker)) {
                return;
            }

            var importState = analyzer.GetAnalysisState(state.Context, _moduleMoniker, false) ??
                analyzer.GetAnalysisState(null, _moduleMoniker, true);
            if (importState != null) {
                await analyzer.AddNotificationAsync(importState, state, cancellationToken);
                await GetImportsAsync(analyzer, state, importState, results, cancellationToken);
            }
        }

        private async Task GetImportsAsync(
            PythonLanguageService analyzer,
            IAnalysisState state,
            IAnalysisState importState,
            RuleResults results,
            CancellationToken cancellationToken
        ) {
            if (importState.Version <= _lastVersion) {
                return;
            }

            var n = string.IsNullOrEmpty(_importName) ? ModuleValue.VariableName : _importName;
            if (n == "*") {
                foreach (var kv in (await importState.GetAllTypesAsync(cancellationToken))) {
                    await results.AddTypesAsync(kv.Key, kv.Value, cancellationToken);
                }
            } else {
                foreach (var target in Targets) {
                    await results.AddTypesAsync(
                        target,
                        await importState.GetTypesAsync(n, cancellationToken),
                        cancellationToken
                    );
                }
            }
            _lastVersion = importState.Version;
            return;
        }

        public override string ToString() {
            return string.Format(
                "from {0} import {1} as {2}",
                _moduleMoniker?.Substring(_moduleMoniker.IndexOf('$') + 1) ?? "(null)",
                string.IsNullOrEmpty(_importName) ? ModuleValue.VariableName : _importName,
                string.Join(", ", Targets)
            );
        }
    }
}
