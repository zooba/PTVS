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
    class ReturnValueLookup : AnalysisRule {
        private readonly CallSiteKey _function;

        public ReturnValueLookup(AnalysisState state, CallSiteKey function, string target) : base(state, target) {
            _function = function;
        }

        public ReturnValueLookup(AnalysisState state, CallSiteKey function, IEnumerable<string> targets) :
            base(state, targets) {
            _function = function;
        }

        protected override async Task ApplyWorkerAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            RuleResults results,
            CancellationToken cancellationToken
        ) {
            var callables = await _function.GetCallableAsync(cancellationToken);
            if (callables == null) {
                return;
            }

            var values = await callables.Call(_function, cancellationToken);

            foreach (var target in Targets) {
                results.AddTypes(target, values);
            }
        }

        public override string ToString() {
            return string.Format("Call{{{0}}} -> {{{1}}}", _function, string.Join(", ", Targets));
        }
    }
}
