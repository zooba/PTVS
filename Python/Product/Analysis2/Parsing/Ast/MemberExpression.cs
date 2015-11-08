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
    public sealed class MemberExpression : Expression {
        private Expression _target;
        private NameExpression _name;

        public MemberExpression() { }

        public Expression Target {
            get { return _target; }
            set { ThrowIfFrozen(); _target = value; }
        }

        public NameExpression NameExpression {
            get { return _name; }
            set { ThrowIfFrozen(); _name = value; }
        }

        public string Name {
            get { return NameExpression?.Name; }
        }

        public override string ToString() {
            return base.ToString() + ":" + (Name ?? "(null)");
        }

        internal override string CheckAssign() {
            return null;
        }

        internal override string CheckDelete() {
            return null;
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _target?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
