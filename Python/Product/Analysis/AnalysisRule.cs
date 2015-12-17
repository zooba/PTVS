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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis {
    abstract class AnalysisRule {
        private readonly AnalysisState _state;
        private readonly object _targets;

        private static readonly IReadOnlyCollection<string> EmptyNames = new string[0];

        protected AnalysisRule(AnalysisState state, string target) {
            _state = state;
            _targets = target;
        }

        protected AnalysisRule(AnalysisState state, IEnumerable<string> targets) {
            _state = state;
            var targetsArray = targets.ToArray();
            if (targetsArray.Length == 0) {
                _targets = null;
            } else if (targetsArray.Length == 1) {
                _targets = targetsArray[0];
            } else {
                _targets = targetsArray;
            }
        }

        public IAnalysisState State => _state;

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

        public async Task<bool> ApplyAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            RuleResults results,
            CancellationToken cancellationToken
        ) {
            var reads = new HashSet<string>();
            var writes = new HashSet<string>();
            using (results.Track(reads, writes)) {
                state.Trace($"  applying rule {this}");
                await ApplyWorkerAsync(analyzer, state, results, cancellationToken);
            }
            if (reads.Any()) {
                state.Trace($"  read: {string.Join(", ", reads.Ordered())}");
            }
            if (writes.Any()) {
                state.Trace($"  changed: {string.Join(", ", writes.Ordered())}");
                return true;
            }
            return false;
        }

        protected abstract Task ApplyWorkerAsync(
            PythonLanguageService analyzer, 
            AnalysisState state,
            RuleResults results,
            CancellationToken cancellationToken
        );
    }
}
