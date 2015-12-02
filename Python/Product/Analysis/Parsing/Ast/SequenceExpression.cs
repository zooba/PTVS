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
    public abstract class SequenceExpression : Expression {
        private IList<SequenceItemExpression> _items;

        protected override void OnFreeze() {
            base.OnFreeze();
            _items = FreezeList(_items);
        }

        public IList<SequenceItemExpression> Items {
            get { return _items; }
            set { ThrowIfFrozen(); _items = value; }
        }

        public int Count => _items?.Count ?? 0;

        public void AddItem(SequenceItemExpression expr) {
            if (_items == null) {
                _items = new List<SequenceItemExpression> { expr };
            } else {
                _items.Add(expr);
            }
        }

        internal override void CheckAssign(Parser parser) {
            for (int i = 0; i < Count; i++) {
                var item = Items[i];
                if (i + 1 < Count || !item.IsExpressionEmpty) {
                    item.CheckAssign(parser);
                }
            }
        }

        internal override void CheckDelete(Parser parser) {
            for (int i = 0; i < Count; i++) {
                var item = Items[i];
                if (i + 1 < Count || !item.IsExpressionEmpty) {
                    item.CheckDelete(parser);
                }
            }
        }

        internal override void CheckAugmentedAssign(Parser parser) {
            parser.ReportError("illegal expression for augmented assignment", Span);
        }

        internal override string CheckName => null;

        private static bool IsComplexAssignment(Expression expr) {
            return !(expr is NameExpression);
        }
    }
}
