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

using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class ComprehensionIf : ComprehensionIterator {
        private readonly Expression _test;
        private int _headerIndex;

        public ComprehensionIf(Expression test) {
            _test = test;
        }

        public Expression Test {
            get { return _test; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_test != null) {
                    _test.Walk(walker);
                }
            }
            walker.PostWalk(this);
        }

        public int HeaderIndex { get { return _headerIndex; } set { _headerIndex = value; } }
    }
}
