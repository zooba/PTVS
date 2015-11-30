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
using Microsoft.PythonTools.Analysis.Values;

namespace Microsoft.PythonTools.Analysis {
    public struct VariableKey {
        public IAnalysisState State;
        public string Key;

        public VariableKey(IAnalysisState state, string key) {
            State = state;
            Key = key;
        }

        public static VariableKey operator +(VariableKey key, string suffix) {
            return new VariableKey(key.State, key.Key + suffix);
        }

        public override int GetHashCode() {
            return (State?.GetHashCode() ?? 0) | (Key?.GetHashCode() ?? 0);
        }

        public override bool Equals(object obj) {
            if (!(obj is VariableKey)) {
                return false;
            }
            var other = (VariableKey)obj;
            return State == other.State && Key == other.Key;
        }

        public override string ToString() {
            return string.Format("<{0}>#{1}", State.Document.Moniker, Key);
        }

        public Task<AnalysisSet> GetTypesAsync(CancellationToken cancellationToken) {
            return State.GetTypesAsync(Key, cancellationToken);
        }

        /// <summary>
        /// A faster call to get variable types when the lock for
        /// <paramref name="callingState"/> is known to be held. Returns
        /// <c>null</c> if the variable belongs to a different state.
        /// </summary>
        internal AnalysisSet GetTypes(AnalysisState callingState) {
            if (callingState == State) {
                Variable variable;
                var key = Key;
                if (callingState.GetVariables().TryGetValue(key, out variable)) {
                    return new AnalysisSet(variable
                        .Types
                        .Concat(callingState.GetRules().SelectMany(r => r.GetTypes(key)))
                        .Where(r => r != AnalysisValue.Empty)
                    );
                }
            }
            return null;
        }
    }
}
