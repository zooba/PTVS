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
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Parsing;

namespace Microsoft.PythonTools.Analysis.Values {
    class OperatorModule : ModuleValue, IDirectAttributeProvider {
        private readonly IReadOnlyDictionary<string, BuiltinFunctionValue> _methods;

        // Methods 'n' with operator.n, operator.__n__, object.__n__ and
        // object.__rn__ (trailing underscores are trimmed when adding dunders)
        private static readonly string[] ReversibleBinaryObjectOperators = new[] {
            "add", "sub", "mul", "div", "truediv", "floordiv", "matmul", "mod",
            "and_", "or_", "xor", "lshift", "rshift"
        };

        // Methods 'n' with operator.in, operator.__in__ and object.__in__
        private static readonly string[] InPlaceBinaryObjectOperators = new[] {
            "add", "sub", "mul", "div", "truediv", "floordiv", "matmul", "mod",
            "and", "or", "xor", "lshift", "rshift"
        };

        // Methods 'n' with operator.n, operator.__n__, object.__n__.
        // Requires a colon followed by arg annotations
        // Names ending in "slice" require HasOperatorSliceFunctions
        private static readonly string[] IrreversibleObjectOperators = new[] {
            "lt:[Any, Any], Any", "le:[Any, Any], Any", "eq:[Any, Any], Any", "ne:[Any, Any], Any",
            "ge:[Any, Any], Any", "gt:[Any, Any], Any",
            "neg:[Any], Any", "pos:[Any], Any", "abs:[Any], Any", "invert:[Any], Any",
            "delitem:[Sequence, Any]", "delslice:[Sequence, Any, Any]",
            "getitem:[Sequence, Any], Any", "getslice:[Sequence, Any, Any], Any",
            "setitem:[Sequence, Any, Any]", "setslice:[Sequence, Any, Any, Any]"
        };

        // Methods 'n' with operator.n, operator.__n__ that return bool
        private static readonly string[] BoolOperators = new[] {
            "is_:[Any, Any], bool", "is_not:[Any, Any]: bool", "not_:[Any], bool"
        };

        private static readonly string[] IntOperators = new[] {
            "index:[Any], int"
        };

        public OperatorModule(VariableKey key, ISourceDocument document, string name)
            : base(key, name, name, document.Moniker) {
            Func<string, VariableKey> K = n => new VariableKey(key.State, n);
            var methods = new Dictionary<string, BuiltinFunctionValue>();

            foreach (var n in ReversibleBinaryObjectOperators) {
                var dunderN = "__" + n.TrimEnd('_') + "__";
                methods[dunderN] = methods[n] = new BuiltinFunctionValue(
                    K(dunderN), "Callable[[Any, Any], Any]", (cs, r, ct) => Arithmetic(cs, dunderN, "__r" + n .TrimEnd('_') + "__", r, ct)
                );
            }

            foreach (var n in InPlaceBinaryObjectOperators) {
                var dunderN = "__i" + n + "__";
                methods[dunderN] = methods["i" + n] = new BuiltinFunctionValue(
                    K(dunderN), "Callable[[Any, Any], Any]", (cs, r, ct) => Arithmetic(cs, dunderN, r, ct)
                );
            }

            foreach (var nWithArgs in IrreversibleObjectOperators) {
                var n = nWithArgs.Remove(nWithArgs.IndexOf(':'));
                var args = nWithArgs.Substring(nWithArgs.IndexOf(':'));

                if (n.EndsWith("slice") && !key.State.Features.HasOperatorSliceFunctions) {
                    continue;
                }

                var dunderN = "__" + n + "__";
                methods[dunderN] = methods[n] = new BuiltinFunctionValue(
                    K(dunderN), "Callable[" + args + "]", (cs, r, ct) => Arithmetic(cs, dunderN, r, ct)
                );
            }

            foreach (var nWithArgs in BoolOperators) {
                var n = nWithArgs.Remove(nWithArgs.IndexOf(':'));
                var args = nWithArgs.Substring(nWithArgs.IndexOf(':'));

                var dunderN = "__" + n.TrimEnd('_') + "__";
                methods[dunderN] = methods[n] = new BuiltinFunctionValue(
                    K(dunderN), "Callable[" + args + "]", BuiltinsModule.ReturnBool
                );
            }

            foreach (var nWithArgs in IntOperators) {
                var n = nWithArgs.Remove(nWithArgs.IndexOf(':'));
                var args = nWithArgs.Substring(nWithArgs.IndexOf(':'));

                var dunderN = "__" + n.TrimEnd('_') + "__";
                methods[dunderN] = methods[n] = new BuiltinFunctionValue(
                    K(dunderN), "Callable[" + args + "]", BuiltinsModule.ReturnInt
                );
            }

            methods["__concat__"] = methods["concat"] = methods["__add__"];
            methods["__iconcat__"] = methods["iconcat"] = methods["__iadd__"];
            methods["__inv__"] = methods["inv"] = methods["__invert__"];

            methods["truth"] = new BuiltinFunctionValue(K("truth"), "Callable[[Any], bool]", BuiltinsModule.ReturnBool);

            _methods = methods;
        }

        public async Task<IReadOnlyCollection<string>> GetAttributeNames(CancellationToken cancellationToken) {
            return _methods.Keys.ToArray();
        }

        public override async Task<IReadOnlyCollection<string>> GetAttributeNames(
            IAnalysisState caller,
            CancellationToken cancellationToken
        ) {
            return _methods.Keys.ToArray();
        }

        public async Task<IAnalysisSet> GetAttribute(string attribute, CancellationToken cancellationToken) {
            BuiltinFunctionValue value;
            if (_methods.TryGetValue(attribute, out value)) {
                return value;
            }
            return AnalysisSet.Empty;
        }

        public override async Task GetAttribute(
            IAnalysisState caller,
            string attribute,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            await result.AddTypesAsync(await GetAttribute(attribute, cancellationToken), cancellationToken);
        }

        public static string GetMemberNameForOperator(PythonOperator op) {
            switch (op) {
                case PythonOperator.Add: return "__add__";
                case PythonOperator.Subtract: return "__sub__";
                case PythonOperator.Multiply: return "__mul__";
                case PythonOperator.Divide: return "__div__";
                case PythonOperator.TrueDivide: return "__truediv__";
                case PythonOperator.FloorDivide: return "__floordiv__";
                case PythonOperator.MatMultiply: return "__matmul__";
                case PythonOperator.Power: return "__pow__";
                case PythonOperator.Mod: return "__mod__";
                case PythonOperator.BitwiseAnd: return "__and__";
                case PythonOperator.BitwiseOr: return "__or__";
                case PythonOperator.BitwiseXor: return "__xor__";
                case PythonOperator.Pos: return "__pos__";
                case PythonOperator.Negate: return "__neg__";
                case PythonOperator.Invert: return "__invert__";
                case PythonOperator.LeftShift: return "__lshift__";
                case PythonOperator.RightShift: return "__rshift__";
                default: return null;
            }
        }

        private static async Task Arithmetic(
            CallSiteKey callSite,
            string opName,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            var arg0 = await callSite.GetArgValue(0, null, cancellationToken);
            var caller = callSite.State;

            var xCls = new LocalAssignable("x.__class__");
            await arg0.GetAttribute(caller, "__class__", xCls, cancellationToken);
            var op = new LocalAssignable(opName);
            await xCls.Values.GetAttribute(caller, opName, op, cancellationToken);

            await op.Values.Call(callSite, result, cancellationToken);
        }

        private static async Task Arithmetic(
            CallSiteKey callSite,
            string opName,
            string ropName,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            var arg0 = await callSite.GetArgValue(0, null, cancellationToken);
            var arg1 = await callSite.GetArgValue(1, null, cancellationToken);

            var caller = callSite.State;

            var xCls = new LocalAssignable("x.__class__");
            await arg0.GetAttribute(caller, "__class__", xCls, cancellationToken);
            var op = new LocalAssignable(opName);
            await xCls.Values.GetAttribute(caller, opName, op, cancellationToken);
            var yCls = new LocalAssignable("y.__class__");
            await arg1.GetAttribute(caller, "__class__", yCls, cancellationToken);
            var rop = new LocalAssignable(ropName);
            await yCls.Values.GetAttribute(caller, ropName, rop, cancellationToken);

            // Reusing callSite is okay because the args match
            await op.Values.Call(callSite, result, cancellationToken);

            // Need to reflect the arguments at this call site
            var rcallSite = new ReflectedCallSiteKey(callSite.CallSite);
            await rop.Values.Call(rcallSite, result, cancellationToken);
        }


        private class ReflectedCallSiteKey : CallSiteKey {
            public ReflectedCallSiteKey(VariableKey site) : base(site) { }

            public override Task<IAnalysisSet> GetArgValue(
                int index,
                string name,
                CancellationToken cancellationToken
            ) {
                if (index == 0) {
                    index = 1;
                } else if (index == 1) {
                    index = 0;
                }
                return base.GetArgValue(index, name, cancellationToken);
            }
        }
    }

    [Export(typeof(IModuleProvider))]
    public sealed class OperatorModuleProvider : IModuleProvider {
        public string Name => "operator";

        public bool TryGetModule(PythonLanguageService analyzer, out IAnalysisState state, out IAnalysisValue module) {
            var sms = SourcelessModuleState.Create(analyzer, Name, (vk, doc) => new OperatorModule(vk, doc, Name));
            state = sms;
            module = sms.Module;
            return true;
        }
    }
}
