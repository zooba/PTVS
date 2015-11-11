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


using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class DecoratorStatement : Statement {
        private Expression _decorator;
        private Statement _inner;

        public Expression Decorator {
            get { return _decorator; }
            set { ThrowIfFrozen(); _decorator = value; }
        }

        public Statement Inner {
            get { return _inner; }
            set { ThrowIfFrozen(); _inner = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _decorator?.Walk(walker);
                _inner?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
