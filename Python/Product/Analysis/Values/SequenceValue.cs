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

namespace Microsoft.PythonTools.Analysis.Values {
    public class SequenceValue : AnalysisValue {
        private readonly VariableKey _contentsKey;

        public SequenceValue(VariableKey key) : base(key) {
            _contentsKey = key + "[:]";
        }

        internal VariableKey ContentsKey => _contentsKey;

        public override async Task<string> ToAnnotationAsync(CancellationToken cancellationToken) {
            var values = await (await _contentsKey.GetTypesAsync(cancellationToken)).ToAnnotationAsync(cancellationToken);
            return string.IsNullOrEmpty(values) ?
                "Sequence" :
                "Sequence[" + values + "]";
        }
    }
}
