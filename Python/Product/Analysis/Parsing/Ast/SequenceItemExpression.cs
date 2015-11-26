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
    public class SequenceItemExpression : ExpressionWithExpression {
        private bool _hasComma;

        public bool HasComma {
            get { return _hasComma; }
            set { ThrowIfFrozen(); _hasComma = value; }
        }

        internal override string CheckName => null;

        internal override void CheckAssign(Parser parser) {
            Expression?.CheckAssign(parser);
        }

        internal override void CheckAugmentedAssign(Parser parser) {
            Expression?.CheckAugmentedAssign(parser);
        }

        internal override void CheckDelete(Parser parser) {
            Expression?.CheckDelete(parser);
        }

        internal override void AppendCodeString(StringBuilder output, PythonAst ast, CodeFormattingOptions format) {
            // TODO: Apply formatting options
            BeforeNode.AppendCodeString(output, ast);
            Expression?.AppendCodeString(output, ast, format);
            if (HasComma) {
                output.Append(',');
            }
            Comment?.AppendCodeString(output, ast, format);
            AfterNode.AppendCodeString(output, ast);
        }
    }
}
