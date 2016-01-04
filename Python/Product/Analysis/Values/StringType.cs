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
using Microsoft.PythonTools.Analysis.Parsing;

namespace Microsoft.PythonTools.Analysis.Values {
    class StringType : TypeValue {
        private readonly bool _isUnicode;
        private readonly Dictionary<string, StringValue> _interned;

        public StringType(VariableKey baseKey, bool isUnicode) :
            base(new VariableKey(baseKey.State, GetName(baseKey, isUnicode)), GetName(baseKey, isUnicode)) {
            _isUnicode = isUnicode;
            _interned = new Dictionary<string, StringValue>();
        }

        private static string GetName(VariableKey key, bool isUnicode) {
            if (key.IsEmpty || key.State.Features.IsUnicodeCalledStr) {
                return isUnicode ? "str" : "bytes";
            } else {
                return isUnicode ? "unicode" : "str";
            }
        }

        public override async Task<string> ToInstanceAnnotationAsync(CancellationToken cancellationToken) {
            return GetName(Key, _isUnicode);
        }

        public AnalysisValue CreateLiteral(ByteString value) {
            StringValue r;
            if (!_interned.TryGetValue(value.String, out r)) {
                _interned[value.String] = r = new StringValue(Key + "$" + value.String, this, value);
            }
            return r;
        }

        public AnalysisValue CreateLiteral(string value) {
            StringValue r;
            if (!_interned.TryGetValue(value, out r)) {
                _interned[value] = r = new StringValue(Key + "$" + value, this, value);
            }
            return r;
        }
    }
}
