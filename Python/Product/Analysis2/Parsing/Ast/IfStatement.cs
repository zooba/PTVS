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
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class IfStatement : Statement {
        private IList<IfStatementTest> _tests;
        private IfStatementTest _else;

        public IfStatement() { }

#if DEBUG
        protected override void OnFreeze() {
            base.OnFreeze();
            _tests = _tests != null ? new ReadOnlyCollection<IfStatementTest>(_tests) : null;
        }
#endif

        public IList<IfStatementTest> Tests {
            get { return _tests; }
            set { ThrowIfFrozen(); _tests = value; }
        }

        internal void AddTest(IfStatementTest test) {
            if (_tests == null) {
                _tests = new List<IfStatementTest>();
            }
            _tests.Add(test);
        }

        public IfStatementTest ElseStatement {
            get { return _else; }
            set { ThrowIfFrozen(); _else = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_tests != null) {
                    foreach (IfStatementTest test in _tests) {
                        test.Walk(walker);
                    }
                }
                _else?.Walk(walker);
            }
            walker.PostWalk(this);
        }

        internal override void AppendCodeString(StringBuilder output, PythonAst ast, CodeFormattingOptions format) {
            // TODO: Apply formatting options
            var t = ast.Tokenization;
            BeforeNode.AppendCodeString(output, ast);
            foreach (var test in Tests ?? Enumerable.Empty<IfStatementTest>()) {
                test.AppendCodeString(output, ast, format);
            }
            ElseStatement?.AppendCodeString(output, ast, format);
            Comment?.AppendCodeString(output, ast, format);
            AfterNode.AppendCodeString(output, ast);
        }
    }
}
