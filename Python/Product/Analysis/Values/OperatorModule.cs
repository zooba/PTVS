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
    class OperatorModule : ModuleValue {
        private readonly IReadOnlyDictionary<string, BuiltinFunctionValue> _methods;

        public OperatorModule(VariableKey key, ISourceDocument document, string name)
            : base(key, name, name, document.Moniker) {
            Func<string, VariableKey> K = n => new VariableKey(key.State, n);
            var methods = new Dictionary<string, BuiltinFunctionValue>();

            foreach (var n in new[] { "add", "sub", "mul", "div", "truediv", "floordiv", "matmul" }) {
                var dunderN = "__" + n + "__";
                methods[dunderN] = methods[n] = new BuiltinFunctionValue(
                    K(dunderN), "Callable[[Any, Any], Any]", (cs, ct) => Arithmetic(cs, dunderN, "__r" + n + "__", ct)
                );
            }

            _methods = methods;
        }

        public override async Task<IReadOnlyCollection<string>> GetAttributeNames(
            IAnalysisState caller,
            CancellationToken cancellationToken
        ) {
            return _methods.Keys.ToArray();
        }

        public override async Task<IAnalysisSet> GetAttribute(
            IAnalysisState caller,
            string attribute,
            CancellationToken cancellationToken
        ) {
            BuiltinFunctionValue value;
            if (_methods.TryGetValue(attribute, out value)) {
                return value;
            }
            return AnalysisSet.Empty;
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
                default: return null;
            }
        }

        private static async Task<IAnalysisSet> Arithmetic(
            CallSiteKey callSite,
            string opName,
            string ropName,
            CancellationToken cancellationToken
        ) {
            var arg0 = await callSite.GetArgValue(0, null, cancellationToken);
            var arg1 = await callSite.GetArgValue(1, null, cancellationToken);

            var caller = callSite.State;

            var result = new AnalysisSet();
            var xCls = await arg0.GetAttribute(caller, "__class__", cancellationToken);
            var op = await xCls.GetAttribute(caller, opName, cancellationToken);
            var yCls = await arg1.GetAttribute(caller, "__class__", cancellationToken);
            var rop = await yCls.GetAttribute(caller, ropName, cancellationToken);

            // Reusing callSite is okay because the args match
            result.AddRange(await op.Call(callSite, cancellationToken));

            // TODO: Handle NotImplemented
            //callSite.State.Analyzer.BuiltinsModule.NotImplemented

            // Need to reflect the arguments at this call site
            var rcallSite = new ReflectedCallSiteKey(callSite.CallSite);
            result.AddRange(await rop.Call(rcallSite, cancellationToken));

            return result;
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
