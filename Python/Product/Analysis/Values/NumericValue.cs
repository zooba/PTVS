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
            _members["__add__"] = new BuiltinFunctionValue(Key + ".__add__", "Callable", AddSubMul);
            _members["__radd__"] = new BuiltinFunctionValue(Key + ".__radd__", "Callable", AddSubMul);
            _members["__sub__"] = new BuiltinFunctionValue(Key + ".__sub__", "Callable", AddSubMul);
            _members["__rsub__"] = new BuiltinFunctionValue(Key + ".__rsub__", "Callable", AddSubMul);
            _members["__mul__"] = new BuiltinFunctionValue(Key + ".__mul__", "Callable", AddSubMul);
            _members["__rmul__"] = new BuiltinFunctionValue(Key + ".__rmul__", "Callable", AddSubMul);
            _members["__div__"] = new BuiltinFunctionValue(Key + ".__div__", "Callable", Div);
            _members["__rdiv__"] = new BuiltinFunctionValue(Key + ".__rdiv__", "Callable", Div);
            _members["__truediv__"] = new BuiltinFunctionValue(Key + ".__truediv__", "Callable", TrueDiv);
            _members["__rtruediv__"] = new BuiltinFunctionValue(Key + ".__rtruediv__", "Callable", TrueDiv);
            _members["__floordiv__"] = new BuiltinFunctionValue(Key + ".__floordiv__", "Callable", ModFloorDiv);
            _members["__rfloordiv__"] = new BuiltinFunctionValue(Key + ".__rfloordiv__", "Callable", ModFloorDiv);
            _members["__mod__"] = new BuiltinFunctionValue(Key + ".__mod__", "Callable", ModFloorDiv);
            _members["__rmod__"] = new BuiltinFunctionValue(Key + ".__rmod__", "Callable", ModFloorDiv);
            _members["__divmod__"] = new BuiltinFunctionValue(Key + ".__mod__", "Callable", DivMod);
            _members["__rdivmod__"] = new BuiltinFunctionValue(Key + ".__rmod__", "Callable", DivMod);
        }

        private static async Task<IAnalysisSet> CoerceAsync(
            CallSiteKey callSite,
            CancellationToken cancellationToken,
            Func<NumericValue, NumericValue, PythonLanguageService, BuiltinsModule, NumericValue> coerce
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
                    var i = coerce(xnv, ynv, caller.Analyzer, bm);
                    if (i != null) {
                        r.Add(i.Instance);
                    }
                }
            }
            return r.Trim();
        }

        private static Task<IAnalysisSet> AddSubMul(CallSiteKey callSite, CancellationToken cancellationToken) {
            return CoerceAsync(callSite, cancellationToken, (x, y, a, bm) => {
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

        private static Task<IAnalysisSet> Div(CallSiteKey callSite, CancellationToken cancellationToken) {
            return CoerceAsync(callSite, cancellationToken, (x, y, a, bm) => {
                if (x == bm.Bool || x == bm.Int) {
                    if (y == bm.Bool || y == bm.Int) {
                        return bm.Int;
                    }
                } else if (x == bm.Long) {
                    if (y == bm.Bool || y == bm.Int) {
                        return bm.Long;
                    }
                } else if (x == bm.Float) {
                    if (y == bm.Bool || y == bm.Int || y == bm.Long) {
                        return bm.Float;
                    }
                } else if (x == bm.Complex) {
                    return bm.Complex;
                }
                return y;
            });
        }

        private static Task<IAnalysisSet> ModFloorDiv(CallSiteKey callSite, CancellationToken cancellationToken) {
            return CoerceAsync(callSite, cancellationToken, (x, y, a, bm) => {
                if (x == bm.Complex || y == bm.Complex) {
                    // Cannot take floor div of complex
                    return null;
                } else if (x == bm.Float || y == bm.Float) {
                    return bm.Float;
                } else if (x == bm.Long || y == bm.Long) {
                    return bm.Long;
                }
                return bm.Int;
            });
        }

        private static Task<IAnalysisSet> TrueDiv(CallSiteKey callSite, CancellationToken cancellationToken) {
            return CoerceAsync(callSite, cancellationToken, (x, y, a, bm) => {
                if (x == bm.Complex || y == bm.Complex) {
                    // Cannot take floor div of complex
                    return bm.Complex;
                }
                return bm.Float;
            });
        }

        private static async Task<IAnalysisSet> DivMod(CallSiteKey callSite, CancellationToken cancellationToken) {
            var res = await ModFloorDiv(callSite, cancellationToken);
            // TODO: Return tuple (res, res)
            return null;
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
