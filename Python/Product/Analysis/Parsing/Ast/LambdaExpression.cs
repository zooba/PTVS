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
    public class LambdaExpression : ExpressionWithExpression {
        private ParameterList _parameters;
        private SourceSpan _beforeColon;
        private bool _generator;

        protected override void OnFreeze() {
            base.OnFreeze();
            _parameters?.Freeze();
        }

        public ParameterList Parameters {
            get { return _parameters; }
            set { ThrowIfFrozen(); _parameters = value; }
        }

        public SourceSpan BeforeColon {
            get { return _beforeColon; }
            set { ThrowIfFrozen(); _beforeColon = value; }
        }

        public bool IsGenerator {
            get { return _generator; }
            set { ThrowIfFrozen(); _generator = value; }
        }

        internal override void AppendCodeString(StringBuilder output, PythonAst ast, CodeFormattingOptions format) {
            // TODO: Apply formatting options
            var t = ast.Tokenization;
            BeforeNode.AppendCodeString(output, ast);
            output.Append("lambda");
            Parameters?.AppendCodeString(output, ast, format);
            BeforeColon.AppendCodeString(output, ast);
            output.Append(":");
            Expression?.AppendCodeString(output, ast, format);
            AfterNode.AppendCodeString(output, ast);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _parameters?.Walk(walker);
                base.Walk(walker);
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => "lambda";
    }
}
