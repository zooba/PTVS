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
    public class CallExpression : ExpressionWithExpression {
        private IList<Arg> _args;
        private CommentExpression _firstComment;

        protected override void OnFreeze() {
            base.OnFreeze();
            _args = FreezeList(_args);
            _firstComment?.Freeze();
        }

        public IList<Arg> Args {
            get { return _args; }
            set { ThrowIfFrozen(); _args = value; }
        }

        public void AddArgument(Arg arg) {
            if (_args == null) {
                _args = new List<Arg>();
            }
            _args.Add(arg);
        }

        internal CommentExpression FirstComment {
            get { return _firstComment; }
            set { ThrowIfFrozen(); _firstComment = value; }
        }

        public bool NeedsLocalsDictionary() {
            var nameExpr = Expression as NameExpression;
            if (nameExpr == null) return false;

            if (_args.Count == 0) {
                if (nameExpr.Name == "locals") return true;
                if (nameExpr.Name == "vars") return true;
                if (nameExpr.Name == "dir") return true;
                return false;
            } else if (_args.Count == 1 && (nameExpr.Name == "dir" || nameExpr.Name == "vars")) {
                if (_args[0].Name == "*" || _args[0].Name == "**") {
                    // could be splatting empty list or dict resulting in 0-param call which needs context
                    return true;
                }
            } else if (_args.Count == 2 && (nameExpr.Name == "dir" || nameExpr.Name == "vars")) {
                if (_args[0].Name == "*" && _args[1].Name == "**") {
                    // could be splatting empty list and dict resulting in 0-param call which needs context
                    return true;
                }
            } else {
                if (nameExpr.Name == "eval") return true;
                if (nameExpr.Name == "execfile") return true;
            }
            return false;
        }

        internal override string CheckName => "function call";

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                base.Walk(walker);
                if (_args != null) {
                    foreach (Arg arg in _args) {
                        arg.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }
    }
}
