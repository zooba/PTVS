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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;

namespace Microsoft.PythonTools.Analysis.Values {
    class BuiltinFunctionValue : CallableValue {
        public delegate Task CallDelegate(
            CallSiteKey callSite,
            IAssignable result,
            CancellationToken cancellationToken
        );

        private readonly string _annotation;
        private readonly CallDelegate _onCall;

        public BuiltinFunctionValue(VariableKey key, string annotation, CallDelegate onCall) : base(key) {
            _annotation = annotation;
            _onCall = onCall;
        }

        public override Task<string> ToAnnotationAsync(CancellationToken cancellationToken) {
            return Task.FromResult(_annotation);
        }

        public override Task Call(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            if (_onCall == null) {
                return Task.FromResult<object>(null);
            }
            return _onCall(callSite, result, cancellationToken);
        }

        public override async Task GetDescriptor(
            IAnalysisState caller,
            VariableKey instance,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            var self = instance.GetTypes(caller) ?? await instance.GetTypesAsync(cancellationToken);
            await MethodWrapperValue.Create(result, this, self, cancellationToken);
        }
    }
}
