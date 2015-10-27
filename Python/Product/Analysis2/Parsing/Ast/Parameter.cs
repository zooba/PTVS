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
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    /// <summary>
    /// Parameter base class
    /// </summary>
    public class Parameter : Node {
        private NameExpression _name;
        private ParameterKind _kind;
        private Expression _defaultValue, _annotation;

        public Parameter() { }

        public NameExpression NameExpression {
            get { return _name; }
            set { ThrowIfFrozen(); _name = value; }
        }

        public string Name {
            get { return _name?.Name; }
        }

        public Expression DefaultValue {
            get { return _defaultValue; }
            set { ThrowIfFrozen(); _defaultValue = value; }
        }

        public Expression Annotation {
            get { return _annotation; }
            set { ThrowIfFrozen(); _annotation = value; }
        }

        public bool IsList {
            get { return _kind == ParameterKind.List; }
        }

        public bool IsDictionary {
            get { return _kind == ParameterKind.Dictionary; }
        }

        public bool IsKeywordOnly {
            get { return _kind == ParameterKind.KeywordOnly; }
        }

        internal ParameterKind Kind {
            get { return _kind; }
            set { ThrowIfFrozen(); _kind = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _annotation?.Walk(walker);
                _defaultValue?.Walk(walker);
            }
            walker.PostWalk(this);
        }

        public PythonVariable GetVariable(PythonAst ast) {
            object reference;
            if (ast.TryGetAttribute(this, PythonVariable.AstKey, out reference)) {
                return (PythonVariable)reference;
            }
            return null;
        }
    }
}
