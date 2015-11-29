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
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;

namespace Microsoft.PythonTools.Analysis.Values {
    sealed class Variable {
        private readonly AnalysisState _state;
        private readonly string _key;
        // TODO: Make this a set
        private readonly AnalysisDictionary<AnalysisValue, object> _types;

        internal Variable(AnalysisState state, string key) {
            _state = state;
            _key = key;
            _types = new AnalysisDictionary<AnalysisValue, object>();
        }

        public IAnalysisState State => _state;
        public VariableKey Key => new VariableKey(_state, _key);

        public IEnumerable<AnalysisValue> Types => _types.Keys;

        internal void AddType(AnalysisValue type) {
            if (type == null) {
                return;
            }
            if (!_types.ContainsKey(type)) {
                _types[type] = null;
            }
        }

        public string ToAnnotationString(IAnalysisState state) {
            return string.Format("{{{0}}}", string.Join(", ", _types.Keys.Select(k => k.ToAnnotation(state))));
        }
    }
}
