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

namespace Microsoft.PythonTools.Analysis {
    class LocalAssignable : IAssignable {
        private readonly Dictionary<string, IAnalysisSet> _variables;
        private readonly string _self;

        public LocalAssignable(string self) {
            _self = self;
            _variables = new Dictionary<string, IAnalysisSet>();
            _variables[_self] = new AnalysisSet();
        }

        private LocalAssignable(string self, Dictionary<string, IAnalysisSet> dict) {
            _self = self;
            _variables = dict;
            _variables[_self] = new AnalysisSet();
        }

        public IAnalysisSet Values => ((AnalysisSet)_variables[_self]).Trim();
        public IReadOnlyDictionary<string, IAnalysisSet> Variables => _variables;

        public IEnumerable<VariableKey> Keys => Enumerable.Empty<VariableKey>();

        public Task AddTypesAsync(IAnalysisSet values, CancellationToken cancellationToken) {
            _variables[_self].AddRange(values);
            return Task.FromResult<object>(null);
        }

        public Task AddTypeAsync(VariableKey key, IAnalysisSet values, CancellationToken cancellationToken) {
            return AddTypesAsync(values, cancellationToken);
        }

        public IAssignable WithSuffix(string suffix) {
            return new LocalAssignable(_self + suffix, _variables);
        }
    }
}
