/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Diagnostics;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public abstract class Node {
        internal static readonly Node EndOfLine = new SingletonNode("<EOL>");

        private SourceSpan _span, _beforeNode, _beforeComment, _comment, _afterNode.

        internal Node() { }

#if DEBUG
        private bool _frozen;
#endif

        [Conditional("DEBUG")]
        internal void Freeze() {
#if DEBUG
            _frozen = true;
#endif
        }

#if DEBUG
        protected virtual void OnFreeze() { };
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

        internal SourceSpan BeforeComment {
            get { return _beforeComment; }
            set { ThrowIfFrozen(); _beforeComment = value; }
        }

        internal SourceSpan Comment {
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
            output.Append(t.GetTokenText(BeforeComment));
            output.Append(t.GetTokenText(Comment));
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
