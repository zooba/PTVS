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

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class SetExpression : Expression {
        private IList<Expression> _items;

        protected override void OnFreeze() {
            base.OnFreeze();
            _items = FreezeList(_items);
        }

        public IList<Expression> Items {
            get { return _items; }
            set { ThrowIfFrozen(); _items = value; }
        }

        internal void AddItem(Expression item) {
            if (_items == null) {
                _items = new List<Expression> { item };
            } else {
                _items.Add(item);
            }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_items != null) {
                    foreach (Expression s in _items) {
                        s?.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => "set literal";
    }
}
