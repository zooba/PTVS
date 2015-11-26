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
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {

    public class FromImportStatement : Statement {
        private static readonly string[] _star = new[] { "*" };
        private DottedName _root;
        private IList<SequenceItemExpression> _names;
        private SourceSpan _beforeNames;
        private bool _hasParentheses;

        private IList<PythonVariable> _variables;

        protected override void OnFreeze() {
            base.OnFreeze();
            _root?.Freeze();
            _names = FreezeList(_names);
        }

        public DottedName Root {
            get { return _root; }
            set { ThrowIfFrozen(); _root = value; }
        }

        public bool IsFromFuture => _root?.IsFuture ?? false;

        public IList<SequenceItemExpression> Names {
            get { return _names; }
            set { ThrowIfFrozen(); _names = value; }
        }

        public SourceSpan BeforeNames {
            get { return _beforeNames; }
            set { ThrowIfFrozen(); _beforeNames = value; }
        }

        public bool HasParentheses {
            get { return _hasParentheses; }
            set { ThrowIfFrozen(); _hasParentheses = value; }
        }

        /// <summary>
        /// Gets the variables associated with each imported name.
        /// </summary>
        public IList<PythonVariable> Variables {
            get { return _variables; }
            set { _variables = value; }
        }

        public IReadOnlyList<PythonReference> GetReferences(PythonAst ast) {
            return GetVariableReferences(this, ast);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
            }
            walker.PostWalk(this);
        }

        public static string GetImportName(SequenceItemExpression expr) {
            return (
                (expr.Expression as NameExpression) ??
                ((expr.Expression as AsExpression)?.Expression as NameExpression)
            )?.Name;
        }

        public static string GetAsName(SequenceItemExpression expr) {
            return (
                (expr.Expression as NameExpression) ??
                (expr.Expression as AsExpression)?.Name
            )?.Name;
        }

        /// <summary>
        /// Returns a new FromImport statement that is identical to this one but has
        /// removed the specified import statement.  Otherwise preserves any attributes
        /// for the statement.
        /// 
        /// New in 1.1.
        /// <param name="ast">The parent AST whose attributes should be updated for the new node.</param>
        /// <param name="index">The index in Names of the import to be removed.</param>
        /// </summary>
        //public FromImportStatement RemoveImport(PythonAst ast, int index) {
        //    if (index < 0 || index >= _names.Length) {
        //        throw new ArgumentOutOfRangeException("index");
        //    }
        //    if (ast == null) {
        //        throw new ArgumentNullException("ast");
        //    }

        //    NameExpression[] names = new NameExpression[_names.Length - 1];
        //    NameExpression[] asNames = _asNames == null ? null : new NameExpression[_asNames.Length - 1];
        //    var asNameWhiteSpace = this.GetNamesWhiteSpace(ast);
        //    List<string> newAsNameWhiteSpace = new List<string>();
        //    int asIndex = 0;
        //    for (int i = 0, write = 0; i < _names.Length; i++) {
        //        bool includingCurrentName = i != index;

        //        // track the white space, this needs to be kept in sync w/ ToCodeString and how the
        //        // parser creates the white space.

        //        if (asNameWhiteSpace != null && asIndex < asNameWhiteSpace.Length) {
        //            if (write > 0) {
        //                if (includingCurrentName) {
        //                    newAsNameWhiteSpace.Add(asNameWhiteSpace[asIndex++]);
        //                } else {
        //                    asIndex++;
        //                }
        //            } else if (i > 0) {
        //                asIndex++;
        //            }
        //        }

        //        if (asNameWhiteSpace != null && asIndex < asNameWhiteSpace.Length) {
        //            if (includingCurrentName) {
        //                if (newAsNameWhiteSpace.Count == 0) {
        //                    // no matter what we want the 1st entry to have the whitespace after the import keyword
        //                    newAsNameWhiteSpace.Add(asNameWhiteSpace[0]);
        //                    asIndex++;
        //                } else {
        //                    newAsNameWhiteSpace.Add(asNameWhiteSpace[asIndex++]);
        //                }
        //            } else {
        //                asIndex++;
        //            }
        //        }

        //        if (includingCurrentName) {
        //            names[write] = _names[i];

        //            if (_asNames != null) {
        //                asNames[write] = _asNames[i];
        //            }

        //            write++;
        //        }

        //        if (AsNames != null && AsNames[i] != null) {
        //            if (asNameWhiteSpace != null && asIndex < asNameWhiteSpace.Length) {
        //                if (i != index) {
        //                    newAsNameWhiteSpace.Add(asNameWhiteSpace[asIndex++]);
        //                } else {
        //                    asIndex++;
        //                }
        //            }

        //            if (_asNames[i].Name.Length != 0) {
        //                if (asNameWhiteSpace != null && asIndex < asNameWhiteSpace.Length) {
        //                    if (i != index) {
        //                        newAsNameWhiteSpace.Add(asNameWhiteSpace[asIndex++]);
        //                    } else {
        //                        asIndex++;
        //                    }
        //                }
        //            } else {
        //                asIndex++;
        //            }
        //        }
        //    }

        //    if (asNameWhiteSpace != null && asIndex < asNameWhiteSpace.Length) {
        //        // trailing comma
        //        newAsNameWhiteSpace.Add(asNameWhiteSpace[asNameWhiteSpace.Length - 1]);
        //    }

        //    var res = new FromImportStatement(_root, names, asNames, IsFromFuture, ForceAbsolute);
        //    ast.CopyAttributes(this, res);
        //    ast.SetAttribute(res, NodeAttributes.NamesWhiteSpace, newAsNameWhiteSpace.ToArray());

        //    return res;
        //}
    }
}
