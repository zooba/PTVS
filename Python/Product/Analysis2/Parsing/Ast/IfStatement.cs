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

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class IfStatement : CompoundStatement {
        private IList<CompoundStatement> _tests;

        public IfStatement() : base(TokenKind.KeywordIf) { }

        protected override void OnFreeze() {
            base.OnFreeze();
            _tests = FreezeList(_tests);
        }

        public IList<CompoundStatement> Tests {
            get { return _tests; }
            set { ThrowIfFrozen(); _tests = value; }
        }

        internal void AddTest(CompoundStatement test) {
            if (_tests == null) {
                _tests = new List<CompoundStatement>();
            }
            _tests.Add(test);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                base.Walk(walker);
                if (_tests != null) {
                    foreach (CompoundStatement test in _tests) {
                        test.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }

        internal override void AppendCodeString(StringBuilder output, PythonAst ast, CodeFormattingOptions format) {
            base.AppendCodeString(output, ast, format);
            if (Tests?.Any() ?? false) {
                foreach (var test in Tests) {
                    test.AppendCodeString(output, ast, format);
                }
            }
        }
    }
}
