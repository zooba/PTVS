﻿// Python Tools for Visual Studio
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
using Microsoft.PythonTools.Analysis.Values;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    sealed class UpdateRules : QueueItem {
        public UpdateRules(AnalysisState item)
            : base(item) { }

        public override async Task PerformAsync(
            PythonLanguageService analyzer,
            PythonFileContext context,
            CancellationToken cancellationToken
        ) {
            IReadOnlyDictionary<string, Variable> variables = null;
            IReadOnlyCollection<AnalysisRule> rules = null;
            await _item.GetVariablesAndRules((v, r) => {
                variables = v;
                rules = r;
            }, cancellationToken);

            if (variables == null || rules == null) {
                return;
            }

            bool anyChange = true;
            while (anyChange) {
                anyChange = false;
                foreach (var r in rules) {
                    bool change = await r.ApplyAsync(analyzer, _item, cancellationToken);
                    if (change) {
                        anyChange = true;
                    }
                }
            }
        }
    }
}
