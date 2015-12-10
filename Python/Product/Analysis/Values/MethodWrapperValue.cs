//// Python Tools for Visual Studio
//// Copyright(c) Microsoft Corporation
//// All rights reserved.
////
//// Licensed under the Apache License, Version 2.0 (the License); you may not use
//// this file except in compliance with the License. You may obtain a copy of the
//// License at http://www.apache.org/licenses/LICENSE-2.0
////
//// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
//// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
//// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
//// MERCHANTABLITY OR NON-INFRINGEMENT.
////
//// See the Apache Version 2.0 License for specific language governing
//// permissions and limitations under the License.

//using System;
//using System.Collections;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.PythonTools.Analysis.Analyzer;
//using Microsoft.PythonTools.Analysis.Parsing.Ast;

//namespace Microsoft.PythonTools.Analysis.Values {
//    public class MethodWrapperValue : CallableValue {
//        private readonly CallableValue _callable;
//        private readonly VariableKey _self;

//        public MethodWrapperValue(VariableKey key, CallableValue node, VariableKey self) : base(key) {
//            _callable = node;
//            _self = self;
//        }

//        public override Task<IAnalysisSet> Call(
//            IAnalysisState caller,
//            VariableKey callSite,
//            CancellationToken cancellationToken
//        ) {
//            var newArgs = new ListWithSelf(_self, args);
//            return _callable.Call(caller, newArgs, keywordArgs, cancellationToken);
//        }

//        private class ListWithSelf : IReadOnlyList<VariableKey> {
//            private readonly VariableKey _first;
//            private readonly IReadOnlyList<VariableKey> _rest;

//            public ListWithSelf(VariableKey first, IReadOnlyList<VariableKey> rest) {
//                _first = first;
//                _rest = rest;
//            }

//            public VariableKey this[int index] => index == 0 ? _first : _rest[index - 1];
//            public int Count => _rest.Count + 1;
//            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

//            public IEnumerator<VariableKey> GetEnumerator() {
//                yield return _first;
//                foreach (var a in _rest) {
//                    yield return a;
//                }
//            }

//        }
//    }
//}
