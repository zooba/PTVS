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
    public class FunctionValue : CallableValue {
        private readonly FunctionDefinition _node;
        private readonly string _fullName;

        public FunctionValue(VariableKey key, FunctionDefinition node, string fullName) : base(key) {
            _node = node;
            _fullName = fullName;
        }

        public override bool Equals(object obj) {
            return (obj as FunctionValue)?._node == _node;
        }

        public override int GetHashCode() {
            return 261563 ^ _node.GetHashCode();
        }
    }
}