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
using System.Collections.Generic;
using System.Text;
using Microsoft.PythonTools.Analysis;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {

    /// <summary>
    /// Top-level ast for all Python code.  Holds onto the body and the line mapping information.
    /// </summary>
    public sealed class PythonAst : ScopeStatement /*, ILocationResolver*/ {
        private readonly Statement _body;
        private readonly Tokenization _tokenization;
        private readonly Dictionary<Node, Dictionary<object, object>> _attributes = new Dictionary<Node, Dictionary<object, object>>();
        private string _privatePrefix;
        private ErrorResult[] _errors;

#if DEBUG
        internal static readonly object VariableReference = "VariableReference";
#else
        internal static readonly object VariableReference = new object();
#endif

        public PythonAst(
            Statement body,
            Tokenization tokenization
        ) {
            if (body == null) {
                throw new ArgumentNullException("body");
            }
            _tokenization = tokenization;
            _body = body;
        }

        internal void SetErrors(ErrorResult[] errors) {
            _errors = errors;
        }
        
        public Tokenization Tokenization {
            get { return _tokenization; }
        }

        public override string Name {
            get {
                return "<module>";
            }
        }

        /// <summary>
        /// Gets the class name which this AST was parsed under.  The class name is appended to any member
        /// accesses that occur.
        /// </summary>
        public string PrivatePrefix {
            get {
                return _privatePrefix;
            }
            internal set {
                _privatePrefix = value;
            }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _body.Walk(walker);
            }
            walker.PostWalk(this);
        }

        public override Statement Body {
            get { return _body; }
        }

        public PythonLanguageVersion LanguageVersion {
            get { return _tokenization.LanguageVersion; }
        }

        internal bool TryGetAttribute(Node node, object key, out object value) {
            Dictionary<object, object> nodeAttrs;
            if (_attributes.TryGetValue(node, out nodeAttrs)) {
                return nodeAttrs.TryGetValue(key, out value);
            } else {
                value = null;
            }
            return false;
        }

        internal void SetAttribute(Node node, object key, object value) {
            Dictionary<object, object> nodeAttrs;
            if (!_attributes.TryGetValue(node, out nodeAttrs)) {
                nodeAttrs = _attributes[node] = new Dictionary<object, object>();
            }
            nodeAttrs[key] = value;
        }

        /// <summary>
        /// Copies attributes that apply to one node and makes them available for the other node.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        public void CopyAttributes(Node from, Node to) {
            Dictionary<object, object> nodeAttrs;
            if (_attributes.TryGetValue(from, out nodeAttrs)) {
                _attributes[to] = new Dictionary<object, object>(nodeAttrs);
            }
        }

        internal SourceLocation IndexToLocation(int index) {
            return SourceLocation.FromIndex(GlobalParent._tokenization, index);
        }

        #region Name Binding Support

        internal override bool ExposesLocalVariable(PythonVariable variable) {
            return true;
        }

        //internal override void FinishBind(PythonNameBinder binder) {
        //}

        //internal override PythonVariable BindReference(PythonNameBinder binder, string name) {
        //    return EnsureVariable(name);
        //}

        //internal override bool TryBindOuter(ScopeStatement from, string name, bool allowGlobals, out PythonVariable variable) {
        //    if (allowGlobals) {
        //        // Unbound variable
        //        from.AddReferencedGlobal(name);

        //        if (from.HasLateBoundVariableSets) {
        //            // If the context contains unqualified exec, new locals can be introduced
        //            // Therefore we need to turn this into a fully late-bound lookup which
        //            // happens when we don't have a PythonVariable.
        //            variable = null;
        //            return false;
        //        } else {
        //            // Create a global variable to bind to.
        //            variable = EnsureGlobalVariable(name);
        //            return true;
        //        }
        //    }
        //    variable = null;
        //    return false;
        //}

        internal override bool IsGlobal {
            get { return true; }
        }

        /// <summary>
        /// Creates a variable at the global level.  Called for known globals (e.g. __name__),
        /// for variables explicitly declared global by the user, and names accessed
        /// but not defined in the lexical scope.
        /// </summary>
        internal PythonVariable/*!*/ EnsureGlobalVariable(string name) {
            PythonVariable variable;
            if (!TryGetVariable(name, out variable)) {
                variable = CreateVariable(name, VariableKind.Global);
            }

            return variable;
        }


        internal PythonVariable/*!*/ EnsureNonlocalVariable(string name) {
            PythonVariable variable;
            if (!TryGetVariable(name, out variable)) {
                variable = CreateVariable(name, VariableKind.Nonlocal);
            }

            return variable;
        }

        #endregion

        #region ILocationResolver Members

        //LocationInfo ILocationResolver.ResolveLocation(IProjectEntry project, object location) {
        //    Node node = (Node)location;
        //    MemberExpression me = node as MemberExpression;
        //    SourceSpan span;
        //    if (me != null) {
        //        span = me.GetNameSpan(this);
        //    } else {
        //        span = node.GetSpan(this);
        //    }
        //    return new LocationInfo(project, span.Start.Line, span.Start.Column);
        //}

        #endregion
    }
}


