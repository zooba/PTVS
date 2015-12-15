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
    class NumericValue : TypeValue {
        private readonly Dictionary<string, BuiltinFunctionValue> _members;

        public NumericValue(VariableKey key, string name) : base(key, name) {
            _members = new Dictionary<string, BuiltinFunctionValue>();
            _members["__add__"] = new BuiltinFunctionValue(Key + ".__add__", "Callable", AddSub);
            _members["__radd__"] = new BuiltinFunctionValue(Key + ".__radd__", "Callable", AddSub);
            _members["__sub__"] = new BuiltinFunctionValue(Key + ".__sub__", "Callable", AddSub);
            _members["__rsub__"] = new BuiltinFunctionValue(Key + ".__rsub__", "Callable", AddSub);
        }

        private static async Task<IAnalysisSet> CoerceAsync(
            CallSiteKey callSite,
            CancellationToken cancellationToken,
            Func<NumericValue, NumericValue, BuiltinsModule, NumericValue> coerce
        ) {
            var caller = callSite.State;
            var x = await callSite.GetArgValue(0, null, cancellationToken);
            var xCls = await x.GetAttribute(caller, "__class__", cancellationToken);
            var y = await callSite.GetArgValue(1, null, cancellationToken);
            var yCls = await y.GetAttribute(caller, "__class__", cancellationToken);

            var bm = caller.Analyzer.BuiltinsModule;
            var r = new AnalysisSet();
            foreach (var xnv in xCls.OfType<NumericValue>()) {
                foreach (var ynv in yCls.OfType<NumericValue>()) {
                    var i = coerce(xnv, ynv, bm);
                    if (i != null) {
                        r.Add(i.Instance);
                    }
                }
            }
            return r.Trim();
        }

        private static Task<IAnalysisSet> AddSub(
            CallSiteKey callSite,
            CancellationToken cancellationToken
        ) {
            return CoerceAsync(callSite, cancellationToken, (x, y, bm) => {
                if (x == bm.Bool || x == bm.Int) {
                    if (y == bm.Bool || y == bm.Int) {
                        return bm.Int;
                    }
                } else if (x == bm.Long) {
                    if (y == bm.Bool || y == bm.Int || y == bm.Long) {
                        return bm.Long;
                    }
                } else if (x == bm.Float) {
                    if (y != bm.Complex) {
                        return bm.Float;
                    }
                }
                return y;
            });
        }

        public override Task<IAnalysisSet> GetAttribute(
            IAnalysisState caller,
            string attribute,
            CancellationToken cancellationToken
        ) {
            BuiltinFunctionValue member;
            if (_members.TryGetValue(attribute, out member)) {
                return Task.FromResult<IAnalysisSet>(member);
            }
            return base.GetAttribute(caller, attribute, cancellationToken);
        }
    }
}
