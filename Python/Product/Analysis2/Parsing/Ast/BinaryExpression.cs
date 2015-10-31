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
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {

    public partial class BinaryExpression : Expression {
        private Expression _left, _right;
        private PythonOperator _op;
        private SourceSpan _withinOp;

        public BinaryExpression() { }

        public Expression Left {
            get { return _left; }
            set { ThrowIfFrozen(); _left = value; }
        }

        public Expression Right {
            get { return _right; }
            set { ThrowIfFrozen(); _right = value; }
        }

        public PythonOperator Operator {
            get { return _op; }
            set { ThrowIfFrozen(); _op = value; }
        }

        public SourceSpan WithinOperator {
            get { return _withinOp; }
            set { ThrowIfFrozen(); _withinOp = value; }
        }

        private bool IsComparison() {
            switch (_op) {
                case PythonOperator.LessThan:
                case PythonOperator.LessThanOrEqual:
                case PythonOperator.GreaterThan:
                case PythonOperator.GreaterThanOrEqual:
                case PythonOperator.Equal:
                case PythonOperator.NotEqual:
                case PythonOperator.In:
                case PythonOperator.NotIn:
                case PythonOperator.IsNot:
                case PythonOperator.Is:
                    return true;
            }
            return false;
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _left.Walk(walker);
                _right.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
