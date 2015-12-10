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

namespace Microsoft.PythonTools.Analysis.Values {
    class StringValue : TypeValue {
        private readonly bool _isUnicode;

        public StringValue(VariableKey baseKey, bool isUnicode) :
            base(baseKey + "." + GetName(baseKey, isUnicode), GetName(baseKey, isUnicode)) {
            _isUnicode = isUnicode;
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
    }
}
