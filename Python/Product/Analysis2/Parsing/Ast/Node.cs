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
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public abstract class Node {
        internal static readonly Node EndOfLine = new SingletonNode("<EOL>");

        private SourceSpan _span, _beforeNode, _afterNode;
        private CommentExpression _comment;

        internal Node() { }

#if DEBUG
        private bool _frozen;
#endif

        [Conditional("DEBUG")]
        internal void Freeze() {
#if DEBUG
            _frozen = true;
            _comment?.Freeze();
#endif
        }

#if DEBUG
        protected virtual void OnFreeze() { }
#endif

        [Conditional("DEBUG")]
        protected void ThrowIfFrozen() {
#if DEBUG
            if (_frozen) {
                Debug.Fail("Node modified after freezing");
                throw new InvalidOperationException("Node modified after freezing");
            }
#endif
        }

        public SourceSpan Span {
            get { return _span; }
            set { ThrowIfFrozen(); _span = value; }
        }

        internal SourceSpan BeforeNode {
            get { return _beforeNode; }
            set { ThrowIfFrozen(); _beforeNode = value;}
        }

        internal CommentExpression Comment {
            get { return _comment; }
            set { ThrowIfFrozen(); _comment = value; }
        }

        internal SourceSpan AfterNode {
            get { return _afterNode; }
            set { ThrowIfFrozen(); _afterNode = value; }
        }

        public abstract void Walk(PythonWalker walker);

        public override string ToString() {
            return string.Format("<{0}>", GetType().Name);
        }

        public string ToCodeString(PythonAst ast) {
            return ToCodeString(ast, CodeFormattingOptions.Default);
        }

        public string ToCodeString(PythonAst ast, CodeFormattingOptions format) {
            var sb = new StringBuilder();
            AppendCodeString(sb, ast, format);
            return sb.ToString();
        }

        internal virtual void AppendCodeString(StringBuilder output, PythonAst ast, CodeFormattingOptions format) {
            // TODO: Apply formatting options
            var t = ast.Tokenization;
            output.Append(t.GetTokenText(BeforeNode));
            output.Append(t.GetTokenText(Span));
            Comment?.AppendCodeString(output, ast, format);
            output.Append(t.GetTokenText(AfterNode));
        }

        internal static PythonReference GetVariableReference(Node node, PythonAst ast) {
            object reference;
            if (ast.TryGetAttribute(node, PythonAst.VariableReference, out reference)) {
                return (PythonReference)reference;
            }
            return null;
        }

        internal static PythonReference[] GetVariableReferences(Node node, PythonAst ast) {
            object reference;
            if (ast.TryGetAttribute(node, PythonAst.VariableReference, out reference)) {
                return (PythonReference[])reference;
            }
            return null;
        }

        private sealed class SingletonNode : Node {
            private readonly string _name;

            public SingletonNode(string name) {
                _name = name;
                Freeze();
            }

            public override void Walk(PythonWalker walker) { }
            public override string ToString() => _name;
        }
    }
}
