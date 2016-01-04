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

namespace Microsoft.PythonTools.Analysis.Values {
    class ListValue : InstanceValue {
        private readonly VariableKey _contentsKey;

        private static VariableKey GetTypeKey(IAnalysisState state) {
            return state.Analyzer.BuiltinsModule.List.Key;
        }

        public ListValue(VariableKey key) : base(key, GetTypeKey(key.State)) {
            _contentsKey = key + "[:]";
        }

        internal VariableKey ContentsKey => _contentsKey;

        public override async Task<string> ToAnnotationAsync(CancellationToken cancellationToken) {
            var values = await (await _contentsKey.GetTypesAsync(cancellationToken)).ToAnnotationAsync(cancellationToken);
            return string.IsNullOrEmpty(values) ?
                "List" :
                "List[" + values + "]";
        }

        public static TypeValue CreateType(VariableKey parent) {
            var t = new TypeValue(new VariableKey(parent.State, "list"), "list");
            t.AddMember("append", k => new BuiltinFunctionValue(k, "Callable[[List, Any]]", Append));
            t.AddMember("__getitem__", k => new BuiltinFunctionValue(k, "Callable[[List, int], Any]", GetItem));
            return t;
        }

        private static async Task Append(
            CallSiteKey callSite,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            var self = await callSite.GetArgValue(0, null, cancellationToken);
            var value = await callSite.GetArgValue(1, null, cancellationToken);
            foreach (var list in self.OfType<ListValue>()) {
                await list.ContentsKey.AddTypesAsync(value, cancellationToken);
            }
        }

        private static async Task GetItem(
            CallSiteKey callSite,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            var self = await callSite.GetArgValue(0, null, cancellationToken);
            foreach (var list in self.OfType<ListValue>()) {
                var value = list.ContentsKey.GetTypes(callSite.State) ??
                    await list.ContentsKey.GetTypesAsync(cancellationToken);
                await result.AddTypesAsync(value, cancellationToken);
            }
        }
    }
}
