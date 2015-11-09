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
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class NameExpression : Expression {
        public static readonly NameExpression[] EmptyArray = new NameExpression[0];
        public static readonly NameExpression Empty = new NameExpression("");

        private readonly string _name;
        private readonly string _prefix;

        public NameExpression(string name, string prefix = null) {
            _name = name ?? "";
            _prefix = string.IsNullOrEmpty(prefix) ? null : prefix;
        }

        public string Name {
            get { return _name; }
        }

        public string Prefix {
            get { return _prefix; }
        }

        public override string ToString() {
            return base.ToString() + ":" + _name;
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => null;

        public PythonReference GetVariableReference(PythonAst ast) {
            return GetVariableReference(this, ast);
        }

        internal override void AppendCodeString(StringBuilder output, PythonAst ast, CodeFormattingOptions format) {
            BeforeNode.AppendCodeString(output, ast);
            output.Append(Name);
            Comment.AppendCodeString(output, ast, format);
            AfterNode.AppendCodeString(output, ast);
        }
    }
}
