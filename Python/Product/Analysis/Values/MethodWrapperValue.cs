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

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Values {
    public class MethodWrapperValue : CallableValue {
        private MethodWrapperValue(VariableKey key) : base(key) { }

        public static async Task Create(
            IAssignable result,
            IAnalysisSet func,
            IAnalysisSet self,
            CancellationToken cancellationToken
        ) {
            foreach (var key in result.Keys) {
                await result.AddTypeAsync(key, new MethodWrapperValue(key), cancellationToken);
                if (key.State.Features.HasDunderSelfOnMethods) {
                    await result.AddTypeAsync(key + ".__self__", self, cancellationToken);
                    await result.AddTypeAsync(key + ".__func__", func, cancellationToken);
                } else {
                    await result.AddTypeAsync(key + ".im_self", self, cancellationToken);
                    await result.AddTypeAsync(key + ".im_func", func, cancellationToken);
                }
            }
        }

        public override async Task Call(
            CallSiteKey callSite,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            var selfKey = Key + (callSite.State.Features.HasDunderSelfOnMethods ? ".__self__" : ".im_self");
            var self = selfKey.GetTypes(callSite.State) ?? await selfKey.GetTypesAsync(cancellationToken);
            var callKey = Key + (callSite.State.Features.HasDunderSelfOnMethods ? ".__func__" : ".im_func");
            var callable = callKey.GetTypes(callSite.State) ?? await callKey.GetTypesAsync(cancellationToken);
            var newArgs = new BoundCallSiteKey(callSite.CallSite, self);
            await callable.Call(newArgs, result, cancellationToken);
        }

        private class BoundCallSiteKey : CallSiteKey {
            private readonly IAnalysisSet _self;

            public BoundCallSiteKey(VariableKey site, IAnalysisSet self) : base(site) {
                _self = self;
            }

            public override async Task<IAnalysisSet> GetArgValue(
                int index,
                string name,
                CancellationToken cancellationToken
            ) {
                if (index == 0) {
                    return _self;
                }
                return await base.GetArgValue(index - 1, name, cancellationToken);
            }
        }
    }
}
