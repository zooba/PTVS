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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Values;

namespace Microsoft.PythonTools.Analysis {
    public class CallSiteKey {
        public VariableKey CallSite;

        public static readonly CallSiteKey Empty = new CallSiteKey(VariableKey.Empty);

        public CallSiteKey(VariableKey site) {
            CallSite = site;
        }

        public CallSiteKey(IAnalysisState state, string key) {
            CallSite = new VariableKey(state, key);
        }

        public bool IsEmpty => CallSite.IsEmpty;
        public IAnalysisState State => CallSite.State;

        public override int GetHashCode() => CallSite.GetHashCode();
        public override bool Equals(object obj) => CallSite == (obj as CallSiteKey)?.CallSite;

        public override string ToString() {
            return "Call " + CallSite.ToString();
        }

        public virtual async Task<IAnalysisSet> GetCallableAsync(CancellationToken cancellationToken) {
            return CallSite.GetTypes(CallSite.State) ?? await CallSite.GetTypesAsync(cancellationToken);
        }

        public virtual Task<IAnalysisSet> GetArgValue(ParameterValue parameter, CancellationToken cancellationToken) {
            return DefaultGetArgValue(parameter.Index, null, cancellationToken);
        }

        public virtual Task<IAnalysisSet> GetArgValue(int index, string name, CancellationToken cancellationToken) {
            return DefaultGetArgValue(index, name, cancellationToken);
        }

        protected async Task<IAnalysisSet> DefaultGetArgValue(
            int index,
            string name,
            CancellationToken cancellationToken
        ) {
            VariableKey pKey;
            IAnalysisSet values;
            if (index >= 0) {
                pKey = CallSite + string.Format("#${0}", index);
                values = pKey.GetTypes(CallSite.State) ?? await pKey.GetTypesAsync(cancellationToken);
                if (values != null && values.Any()) {
                    return values;
                }
            }
            if (!string.IsNullOrEmpty(name)) {
                pKey = CallSite + ("#" + name);
                return pKey.GetTypes(CallSite.State) ?? await pKey.GetTypesAsync(cancellationToken);
            }
            return AnalysisSet.Empty;
        }
    }
}
