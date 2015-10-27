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
using System.Diagnostics;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class LambdaExpression : Expression {
        private ParameterList _parameters;
        private Expression _expression;
        private SourceSpan _beforeColon;

        public LambdaExpression() {
        }

        public ParameterList Parameters {
            get { return _parameters; }
            set { ThrowIfFrozen(); _parameters = value; }
        }

        public Expression Expression {
            get { return _expression; }
            set { ThrowIfFrozen(); _expression = value; }
        }

        public SourceSpan BeforeColon {
            get { return _beforeColon; }
            set { ThrowIfFrozen(); _beforeColon = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _parameters?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
