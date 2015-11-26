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
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    /// <summary>
    /// Parameter base class
    /// </summary>
    public class Parameter : Node {
        private NameExpression _name;
        private ParameterKind _kind;
        private Expression _defaultValue, _annotationOrSublist;
        private bool _hasCommaBeforeComment, _hasCommaAfterNode;

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
            get { return _annotationOrSublist; }
            set { ThrowIfFrozen(); _annotationOrSublist = value; }
        }

        public TupleExpression Sublist {
            get { return IsSublist ? (TupleExpression)_annotationOrSublist : null; }
            set { ThrowIfFrozen(); _annotationOrSublist = value; }
        }

        public bool HasCommaBeforeComment {
            get { return _hasCommaBeforeComment; }
            set { ThrowIfFrozen(); _hasCommaBeforeComment = value; }
        }

        public bool HasCommaAfterNode {
            get { return _hasCommaAfterNode; }
            set { ThrowIfFrozen(); _hasCommaAfterNode = value; }
        }

        public bool IsEmpty => _name == null;

        public bool IsList => _kind == ParameterKind.List;

        public bool IsDictionary => _kind == ParameterKind.Dictionary;

        public bool IsKeywordOnly => _kind == ParameterKind.KeywordOnly;

        public bool IsSublist => _kind == ParameterKind.Sublist;

        internal ParameterKind Kind {
            get { return _kind; }
            set { ThrowIfFrozen(); _kind = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _annotationOrSublist?.Walk(walker);
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
