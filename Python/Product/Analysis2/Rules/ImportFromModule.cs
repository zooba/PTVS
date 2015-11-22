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
        private readonly IReadOnlyCollection<KeyValuePair<string, string>> _importNames;

        private long _lastVersion;

        public ImportFromModule(
            string moduleMoniker,
            IReadOnlyCollection<string> importNames,
            IReadOnlyCollection<string> targetNames
        ) {
            _moduleMoniker = moduleMoniker;
            _importNames = importNames.Zip(targetNames, (i, t) => new KeyValuePair<string, string>(i, t)).ToArray();
        }

        protected override async Task<Dictionary<string, IReadOnlyCollection<AnalysisValue>>> ApplyWorkerAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            IReadOnlyDictionary<string, IReadOnlyCollection<AnalysisValue>> priorResults,
            CancellationToken cancellationToken
        ) {
            var importState = await analyzer.GetAnalysisStateAsync(
                state.Context,
                _moduleMoniker,
                true,
                cancellationToken
            );
            if (importState == null || importState.Version <= _lastVersion) {
                return null;
            }

            var results = new Dictionary<string, IReadOnlyCollection<AnalysisValue>>();
            foreach (var name in _importNames) {
                if (string.IsNullOrEmpty(name.Key)) {
                    // TODO: Add module as value
                } else {
                    results[name.Value] = await importState.GetTypesAsync(name.Key, cancellationToken);
                }
            }
            _lastVersion = importState.Version;
            return results;
        }
    }
}
