/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {

    public sealed class SuiteStatement : Statement {
        private readonly IReadOnlyList<Statement> _statements;

        public SuiteStatement(IReadOnlyList<Statement> statements) {
            _statements = statements;
        }

        public IReadOnlyList<Statement> Statements {
            get { return _statements; }
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

            var res = new SuiteStatement(statements);

            return res;
        }
    }
}
