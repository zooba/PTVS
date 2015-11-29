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
    class BuiltinFunctionInfo : AnalysisValue {
        public delegate IReadOnlyList<AnalysisValue> CallDelegate(
            IReadOnlyList<IReadOnlyList<AnalysisValue>> args,
            IReadOnlyDictionary<string, IReadOnlyList<AnalysisValue>> keywordArgs,
            Func<string, IReadOnlyList<AnalysisValue>> getVariable
        );

        private readonly string _annotation;
        private readonly CallDelegate _onCall;

        public BuiltinFunctionInfo(string annotation, CallDelegate onCall) : base(BuiltinTypes.Function) {
            _annotation = annotation;
            _onCall = onCall;
        }

        public override string ToAnnotation(IAnalysisState state) {
            return _annotation;
        }

        public IReadOnlyList<AnalysisValue> Call(
            IReadOnlyList<IReadOnlyList<AnalysisValue>> args,
            IReadOnlyDictionary<string, IReadOnlyList<AnalysisValue>> keywordArgs,
            Func<string, IReadOnlyList<AnalysisValue>> getVariable
        ) {
            return _onCall(args, keywordArgs, getVariable);
        }
    }
}
