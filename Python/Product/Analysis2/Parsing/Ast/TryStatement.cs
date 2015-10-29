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
    public class TryStatement : Statement {
        private Statement _body, _else, _finally;
        private IList<TryStatementHandler> _handlers;

        public TryStatement() { }

        public Statement Body {
            get { return _body; }
            set { ThrowIfFrozen(); _body = value; }
        }

        public IList<TryStatementHandler> Handlers {
            get { return _handlers; }
            set { ThrowIfFrozen(); _handlers = value; }
        }

        public void AddHandler(TryStatementHandler handler) {
            if (_handlers == null) {
                _handlers = new List<TryStatementHandler>();
            }
            _handlers.Add(handler);
        }

        protected override void OnFreeze() {
            base.OnFreeze();
            _handlers = FreezeList(_handlers);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _body?.Walk(walker);
                if (_handlers != null) {
                    foreach (TryStatementHandler handler in _handlers) {
                        handler.Walk(walker);
                    }
                }
                _else?.Walk(walker);
                _finally?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }

    // A handler corresponds to the except block.
    public class TryStatementHandler : Node {
        private readonly TokenKind _kind;
        private Expression _test, _target;
        private Statement _body;
        private SourceSpan _afterComment;

        public TryStatementHandler(TokenKind kind) {
            _kind = kind;
        }

        public TokenKind Kind {
            get { return _kind; }
        }

        public Expression Test {
            get { return _test; }
            set { ThrowIfFrozen(); _test = value; }
        }

        public Expression Target {
            get { return _target; }
            set { ThrowIfFrozen(); _target = value; }
        }

        public Statement Body {
            get { return _body; }
            set { ThrowIfFrozen(); _body = value; }
        }

        public SourceSpan AfterComment {
            get { return _afterComment; }
            set { ThrowIfFrozen(); _afterComment = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _test?.Walk(walker);
                _target?.Walk(walker);
                _body?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
