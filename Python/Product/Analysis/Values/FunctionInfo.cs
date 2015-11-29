﻿// Python Tools for Visual Studio
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
    public class FunctionInfo : AnalysisValue {
        private readonly FunctionDefinition _node;
        private readonly string _fullName, _key;

        public FunctionInfo(FunctionDefinition node, string fullName) : base(BuiltinTypes.Function) {
            _node = node;
            _fullName = fullName;
            _key = string.Format("{0}@{1}", fullName, node.Span.Start.Index);
        }

        public string Key => _key;

        public override bool Equals(object obj) {
            return (obj as FunctionInfo)?._node == _node;
        }

        public override int GetHashCode() {
            return 261563 ^ _node.GetHashCode();
        }

        public override string ToAnnotation(IAnalysisState state) {
            
            return "Callable";
        }

        public IReadOnlyList<AnalysisValue> Call(
            VariableKey self,
            IReadOnlyList<IReadOnlyList<AnalysisValue>> args,
            IReadOnlyDictionary<string, IReadOnlyList<AnalysisValue>> keywordArgs,
            Func<string, IReadOnlyList<AnalysisValue>> getVariable
        ) {
            return null;
        }
    }
}
