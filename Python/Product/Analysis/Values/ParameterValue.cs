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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Values {
    public class ParameterValue : AnalysisValue {
        private readonly ParameterKind _kind;
        private readonly int _index;

        private static VariableKey GetKey(VariableKey callable, ParameterKind kind, int index, string suffix = "") {
            switch (kind) {
                case ParameterKind.List:
                    return callable + suffix + "$*";
                case ParameterKind.Dictionary:
                    return callable + suffix + "$**";
                case ParameterKind.KeywordOnly:
                    throw new InvalidOperationException("cannot create ParameterValue for keyword parameter");
                default:
                    return callable + suffix + string.Format("${0}", index);
            }
        }

        public ParameterValue(VariableKey callable, ParameterKind kind, int index) :
            base(GetKey(callable, kind, index)) {
            _kind = kind;
            _index = index;
        }

        public ParameterKind Kind => _kind;
        public int Index => _index;

        public override async Task<string> ToAnnotationAsync(CancellationToken cancellationToken) {
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
