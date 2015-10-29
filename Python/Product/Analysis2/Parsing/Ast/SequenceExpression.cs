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
using System.Collections.ObjectModel;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public abstract class SequenceExpression : Expression {
        private IList<Expression> _items;

        protected SequenceExpression() { }

        protected override void OnFreeze() {
            base.OnFreeze();
            _items = FreezeList(_items);
        }

        public IList<Expression> Items {
            get { return _items; }
            set { ThrowIfFrozen(); _items = value; }
        }

        public void AddItem(Expression expr) {
            if (_items == null) {
                _items = new List<Expression>();
            }
            _items.Add(expr);
        }

        internal override string CheckAssign() {
            for (int i = 0; i < Items.Count; i++) {
                Expression e = Items[i];
                if (e.CheckAssign() != null) {
                    // we don't return the same message here as CPython doesn't seem to either, 
                    // for example ((yield a), 2,3) = (2,3,4) gives a different error than
                    // a = yield 3 = yield 4.
                    return "can't assign to " + e.GetType().Name;
                }
            }
            return null;

        }

        internal override string CheckDelete() {
            for (int i = 0; i < Items.Count; i++) {
                Expression e = Items[i];
                if (e.CheckDelete() != null) {
                    // we don't return the same message here as CPython doesn't seem to either, 
                    // for example ((yield a), 2,3) = (2,3,4) gives a different error than
                    // a = yield 3 = yield 4.
                    return "can't delete " + e.GetType().Name;
                }
            }
            return null;
        }

        internal override string CheckAugmentedAssign() {
            return "illegal expression for augmented assignment";
        }

        private static bool IsComplexAssignment(Expression expr) {
            return !(expr is NameExpression);
        }
    }
}
