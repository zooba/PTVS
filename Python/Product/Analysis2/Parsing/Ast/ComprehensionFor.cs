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
    public class ComprehensionFor : ComprehensionIterator {
        private readonly Expression _lhs, _list;

        public ComprehensionFor(Expression lhs, Expression list) {
            _lhs = lhs;
            _list = list;
        }

        public Expression Left {
            get { return _lhs; }
        }

        public Expression List {
            get { return _list; }
        }
        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_lhs != null) {
                    _lhs.Walk(walker);
                }
                if (_list != null) {
                    _list.Walk(walker);
                }
            }
            walker.PostWalk(this);
        }
    }
}
