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

using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class AsExpression : ExpressionWithExpression {
        private NameExpression _name;

        public NameExpression Name {
            get { return _name; }
            set { ThrowIfFrozen(); _name = value; }
        }

        internal override void AppendCodeString(StringBuilder output, PythonAst ast, CodeFormattingOptions format) {
            // TODO: Apply formatting options
            BeforeNode.AppendCodeString(output, ast);
            Expression?.AppendCodeString(output, ast, format);
            output.Append("as");
            Name?.AppendCodeString(output, ast, format);
            Comment?.AppendCodeString(output, ast, format);
            AfterNode.AppendCodeString(output, ast);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                Expression?.Walk(walker);
                Name?.Walk(walker);
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => "as expression";
    }
}
