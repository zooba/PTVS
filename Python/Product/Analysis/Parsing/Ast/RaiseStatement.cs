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

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class RaiseStatement : StatementWithExpression {
        private Expression _cause;

        public Expression Type {
            get {
                var te = Expression as TupleExpression;
                if (te != null) {
                    if (te.Count >= 1) {
                        return te.Items[0].Expression;
                    }
                    return null;
                }

                return Expression;
            }
        }

        public Expression Value {
            get {
                var te = Expression as TupleExpression;
                if (te != null) {
                    if (te.Count >= 2) {
                        return te.Items[1].Expression;
                    }
                }

                return null;
            }
        }

        public Expression Traceback {
            get {
                var te = Expression as TupleExpression;
                if (te != null) {
                    if (te.Count >= 3) {
                        return te.Items[2].Expression;
                    }
                }

                return null;
            }
        }

        public Expression Cause {
            get { return _cause; }
            set { ThrowIfFrozen(); _cause = value; }
        }

        protected override void OnFreeze() {
            base.OnFreeze();
            _cause?.Freeze();
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                base.Walk(walker);
                _cause?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
