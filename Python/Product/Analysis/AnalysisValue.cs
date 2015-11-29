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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Values;

namespace Microsoft.PythonTools.Analysis {
    public abstract class AnalysisValue {
        public static readonly AnalysisValue Empty = new EmptyAnalysisValue();

        private readonly AnalysisValue _type;

        public AnalysisValue(AnalysisValue type) {
            _type = type;
        }

        public AnalysisValue Type => _type ?? Empty;

        public abstract string ToAnnotation(IAnalysisState state);

        public virtual AnalysisValue GetAttribute(VariableKey self, string attribute) {
            return Empty;
        }

        private class EmptyAnalysisValue : AnalysisValue {
            public EmptyAnalysisValue() : base(null) { }

            public override string ToAnnotation(IAnalysisState state) => string.Empty;
        }
    }
}
