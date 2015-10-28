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

    public sealed class SuiteStatement : Statement {
        private readonly IReadOnlyList<Statement> _statements;
        private readonly SourceSpan _indent;

        public SuiteStatement(IReadOnlyList<Statement> statements, SourceSpan indent) {
            _statements = statements;
            _indent = indent;
        }

        public IReadOnlyList<Statement> Statements {
            get { return _statements; }
        }

        public SourceSpan Indent {
            get { return _indent; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_statements != null) {
                    foreach (Statement s in _statements) {
                        s.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }

        public override string Documentation {
            get {
                return _statements?.FirstOrDefault()?.Documentation;
            }
        }

        /// <summary>
        /// Returns a new SuiteStatement which is composed of a subset of the statements in this suite statement.
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public SuiteStatement CloneSubset(PythonAst ast, int start, int end) {
            Statement[] statements = new Statement[end - start + 1];
            for (int i = start; i <= end; i++) {
                statements[i - start] = Statements[i];
            }

            var res = new SuiteStatement(statements, _indent);

            return res;
        }
    }
}
