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
    // New in Pep380 for Python 3.3. Yield From is an expression with a return value.
    //    x = yield from z
    // The return value (x) is taken from the value attribute of a StopIteration
    // error raised by next(z) or z.send().
    public class YieldFromExpression : ExpressionWithExpression {
        private SourceSpan _beforeFrom;

        public SourceSpan BeforeFrom {
            get { return _beforeFrom; }
            set { ThrowIfFrozen(); _beforeFrom = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                base.Walk(walker);
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => "yield from expression";
    }
}
