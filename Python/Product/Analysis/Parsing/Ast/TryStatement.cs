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
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class TryStatement : CompoundStatement {
        private IList<CompoundStatement> _handlers;

        public TryStatement() : base(TokenKind.KeywordTry) { }

        public IList<CompoundStatement> Handlers {
            get { return _handlers; }
            set { ThrowIfFrozen(); _handlers = value; }
        }

        public void AddHandler(CompoundStatement handler) {
            if (_handlers == null) {
                _handlers = new List<CompoundStatement>();
            }
            _handlers.Add(handler);
        }

        protected override void OnFreeze() {
            base.OnFreeze();
            _handlers = FreezeList(_handlers);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                base.Walk(walker);
                if (_handlers != null) {
                    foreach (CompoundStatement handler in _handlers) {
                        handler.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }
    }
}
