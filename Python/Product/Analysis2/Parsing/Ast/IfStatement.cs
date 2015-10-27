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
using System.Collections.ObjectModel;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class IfStatement : Statement {
        private IList<IfStatementTest> _tests;
        private Statement _else;

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

        public Statement ElseStatement {
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
                if (_else != null) {
                    _else.Walk(walker);
                }
            }
            walker.PostWalk(this);
        }
    }
}
