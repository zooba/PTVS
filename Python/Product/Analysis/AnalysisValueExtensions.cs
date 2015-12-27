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
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis {
    public static class AnalysisValueExtensions {
        public static async Task<string> ToAnnotationAsync(
            this IEnumerable<AnalysisValue> values,
            CancellationToken cancellationToken
        ) {
            var names = await values.AsAnnotationsAsync(cancellationToken);
            return string.Join(", ", names.Ordered());
        }

        public static async Task<IReadOnlyCollection<string>> AsAnnotationsAsync(
            this IEnumerable<AnalysisValue> values,
            CancellationToken cancellationToken
        ) {
            var names = new HashSet<string>();
            foreach (var t in values.MaybeEnumerate()) {
                names.Add(await t.ToAnnotationAsync(cancellationToken));
            }
            return names;
        }

        private static void Add(ref IAnalysisSet set, AnalysisValue value) {
            if (set == null) {
                set = value;
            } else if (set.IsReadOnly) {
                set = set.Clone(false);
                set.Add(value);
            } else {
                set.Add(value);
            }
        }

        private static void Add(ref IAnalysisSet set, IEnumerable<AnalysisValue> value) {
            if (set == null) {
                set = new AnalysisSet(value).Trim();
            } else if (set.IsReadOnly) {
                set = set.Clone(false);
                set.AddRange(value);
            } else {
                set.AddRange(value);
            }
        }

        public static async Task GetAttribute(
            this IEnumerable<AnalysisValue> values,
            IAnalysisState caller,
            string attribute,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            foreach (var t in values) {
                await t.GetAttribute(caller, attribute, result, cancellationToken);
            }
        }

        public static async Task Call(
            this IEnumerable<AnalysisValue> values,
            CallSiteKey callSite,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            foreach (var t in values) {
                await t.Call(callSite, result, cancellationToken);
            }
        }

        public static IAnalysisSet ToSet(this IEnumerable<AnalysisValue> values) {
            return new AnalysisSet(values).Trim();
        }
    }
}
