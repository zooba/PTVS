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
    public class AugmentedAssignStatement : StatementWithExpression {
        private PythonOperator _op;
        private Expression _target;

        protected override void OnFreeze() {
            base.OnFreeze();
            _target?.Freeze();
        }

        public PythonOperator Operator {
            get { return _op; }
            set { ThrowIfFrozen(); _op = value; }
        }

        public Expression Target {
            get { return _target; }
            set { ThrowIfFrozen(); _target = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _target?.Walk(walker);
                base.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
