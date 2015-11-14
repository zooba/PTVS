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
    
    public class ExecStatement : StatementWithExpression {
        private Expression _locals, _globals;

        protected override void OnFreeze() {
            base.OnFreeze();
            _locals?.Freeze();
            _globals?.Freeze();
        }

        public Expression Locals {
            get { return _locals; }
            set { ThrowIfFrozen(); _locals = value; }
        }

        public Expression Globals {
            get { return _globals; }
            set { ThrowIfFrozen(); _globals = value; }
        }

        public bool NeedsLocalsDictionary() {
            return _globals == null && _locals == null;
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                base.Walk(walker);
                _locals?.Walk(walker);
                _globals?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
