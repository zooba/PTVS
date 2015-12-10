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
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis.Values {
    public class InstanceValue : AnalysisValue {
        public InstanceValue(VariableKey type) : base(type) {
        }

        public override async Task<string> ToAnnotationAsync(CancellationToken cancellationToken) {
            var values = await Key.GetTypesAsync(cancellationToken);

            var annotations = new HashSet<string>();
            foreach (var group in values.MaybeEnumerate().GroupBy(v => (v is TypeValue))) {
                if (group.Key) {
                    foreach (var value in group) {
                        annotations.Add(await ((TypeValue)value).ToInstanceAnnotationAsync(cancellationToken));
                    }
                } else {
                    foreach (var value in group) {
                        annotations.Add(await value.ToAnnotationAsync(cancellationToken));
                    }
                }
            }
            if (!annotations.Any()) {
                return "<unknown>";
            }
            return string.Join(", ", annotations.Ordered());
        }
    }
}
