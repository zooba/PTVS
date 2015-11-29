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
using Microsoft.PythonTools.Analysis.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Values {
    class ParameterInfo : AnalysisValue {
        private readonly ParameterKind _kind;
        private readonly int _index;

        private static readonly Dictionary<int, ParameterInfo> _normalCache = new Dictionary<int, ParameterInfo>();

        public static ParameterInfo Create(int index) {
            ParameterInfo r;
            lock (_normalCache) {
                if (!_normalCache.TryGetValue(index, out r)) {
                    _normalCache[index] = r = new ParameterInfo(ParameterKind.Normal, index);
                }
            }
            return r;
        }

        public static readonly ParameterInfo ListParameter = new ParameterInfo(ParameterKind.List, -1);
        public static readonly ParameterInfo DictParameter = new ParameterInfo(ParameterKind.Dictionary, -1);

        public ParameterInfo(ParameterKind kind, int index) : base(null) {
            _kind = kind;
            _index = index;
        }

        public ParameterKind Kind => _kind;
        public int Index => _index;

        public string KeySuffix {
            get {
                if (_kind == ParameterKind.List) {
                    return "$*";
                } else if (_kind == ParameterKind.Dictionary) {
                    return "$**";
                } else {
                    return string.Format("${0}", _index);
                }
            }
        }

        public VariableKey CreateKey(IAnalysisState state, string functionKey, string suffix = "") {
            return new VariableKey(state, functionKey + suffix + KeySuffix);
        }

        public override string ToAnnotation(IAnalysisState state) {
            if (_kind == ParameterKind.List) {
                return "Parameter[*]";
            } else if (_kind == ParameterKind.Dictionary) {
                return "Parameter[**]";
            } else {
                return string.Format("Parameter[{0}]", _index);
            }
        }
    }
}
