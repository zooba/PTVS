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
    public sealed class Arg : Node {
        private Expression _name, _expression;
        private bool _hasCommaAfterNode;

        public Arg() { }

        public string Name {
            get { return (_name as NameExpression)?.Name; }
        }

        protected override void OnFreeze() {
            base.OnFreeze();
            _name?.Freeze();
            _expression?.Freeze();
        }

        public Expression NameExpression {
            get { return _name; }
            set { ThrowIfFrozen(); _name = value; }
        }

        public Expression Expression {
            get { return _expression; }
            set { ThrowIfFrozen(); _expression = value; }
        }

        public bool HasCommaAfterNode {
            get { return _hasCommaAfterNode; }
            set { ThrowIfFrozen(); _hasCommaAfterNode = value; }
        }

        public override string ToString() {
            return base.ToString() + ":" + _name;
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _expression?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
