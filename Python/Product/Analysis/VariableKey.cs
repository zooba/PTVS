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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis {
    [DebuggerTypeProxy(typeof(DebugViewProxy))]
    public struct VariableKey {
        public IAnalysisState State;
        public string Key;

        public static readonly VariableKey Empty = new VariableKey(null, null);

        public VariableKey(IAnalysisState state, string key) {
            State = state;
            Key = key;
        }

        public bool IsEmpty => State == null;

        public static VariableKey operator +(VariableKey key, string suffix) {
            return new VariableKey(key.State, (key.Key ?? string.Empty) + suffix);
        }

        public static bool operator ==(VariableKey x, VariableKey y) {
            return x.State == y.State && x.Key == y.Key;
        }

        public static bool operator !=(VariableKey x, VariableKey y) {
            return x.State != y.State || x.Key != y.Key;
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
            var bits = State?.Document?.Moniker?.Split('\\') ?? Enumerable.Empty<string>();
            var stateName = bits.LastOrDefault() ?? "<unknown>";
            foreach (var bit in bits.Reverse().Skip(1)) {
                if (stateName.Length + 1 + bit.Length > 40) {
                    stateName = "...\\" + stateName;
                    break;
                }
                stateName = bit + "\\" + stateName;
            }
            return string.Format("{0} in {1}", Key, stateName);
        }

        public Task<IAnalysisSet> GetTypesAsync(CancellationToken cancellationToken) {
            return State?.GetTypesAsync(Key, cancellationToken) ??
                Task.FromCanceled<IAnalysisSet>(cancellationToken);
        }

        /// <summary>
        /// A faster call to get variable types when the lock for
        /// <paramref name="callingState"/> is known to be held. Returns
        /// <c>null</c> if the variable belongs to a different state.
        /// </summary>
        internal IAnalysisSet GetTypes(IAnalysisState callingState) {
            var state = callingState as AnalysisState;
            if (state != null && callingState == State) {
                Variable variable = null;
                var key = Key;
                if (state.GetVariables()?.TryGetValue(key, out variable) ?? false && variable != null) {
                    var results = state.GetPendingRuleResults();
                    if (results == null) {
                        return variable.Types;
                    }
                    return variable.Types.Union(results.GetTypes(key));
                }
            }
            return null;
        }

        sealed class DebugViewProxy {
            public DebugViewProxy(VariableKey source) {
                State = source.State;
                Key = source.Key;
                Source = State?.Document?.Moniker;
                // Passing source.State to this is a bit of a nasty hack, but
                // since it's only used while debugging it should be fine.
                Types = source.GetTypes(source.State)?.ToArray();
            }

            public readonly string Source;
            public readonly IAnalysisState State;
            public readonly string Key;

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public readonly AnalysisValue[] Types;
        }

    }
}
