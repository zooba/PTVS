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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;

namespace Microsoft.PythonTools.Analysis.Values {
    public sealed class Variable {
        private readonly VariableKey _key;
        private IAnalysisSet _types;

        internal Variable(AnalysisState state, string key) {
            _key = new VariableKey(state, key);
        }

        public VariableKey Key => _key;
        public long Version => _types.Version;

        public IAnalysisSet Types => _types?.Clone(true) ?? AnalysisSet.Empty;

        internal void AddType(AnalysisValue type) {
            if (type == null) {
                return;
            }
            if (_types == null) {
                _types = type;
                return;
            } else if (_types.IsReadOnly) {
                _types = _types.Clone();
            }
            _types.Add(type);
        }

        internal void AddTypes(IAnalysisSet set) {
            if (set == null || !set.Any()) {
                return;
            }
            if (_types == null) {
                _types = set.Clone();
            } else if (_types.IsReadOnly) {
                _types = _types.Union(set);
            } else {
                _types.AddRange(set);
            }
        }

        public async Task<string> ToAnnotationAsync(CancellationToken cancellationToken) {
            var types = _types;
            if ((types?.Count ?? 0) == 0) {
                return "{}";
            }
            var names = new List<string>(types.Count);
            foreach (var t in types) {
                names.Add(await t.ToAnnotationAsync(cancellationToken));
            }
            return string.Join(", ", names);
        }

        public async Task<string> ToDebugAnnotationAsync(CancellationToken cancellationToken) {
            var types = _types;
            if ((types?.Count ?? 0) == 0) {
                return "{}";
            }
            var names = new List<string>(types.Count);
            foreach (var t in types) {
                names.Add(await t.ToDebugAnnotationAsync(cancellationToken));
            }
            return string.Join(", ", names);
        }
    }
}
