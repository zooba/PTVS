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

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {

    public class ImportStatement : Statement {
        private IList<DottedName> _names;
        private IList<NameExpression> _asNames;

        private IList<PythonVariable> _variables;

        public IList<PythonVariable> Variables {
            get { return _variables; }
            set { _variables = value; }
        }

        public IReadOnlyList<PythonReference> GetReferences(PythonAst ast) {
            return GetVariableReferences(this, ast);
        }

        public IList<DottedName> Names {
            get { return _names; }
            set { ThrowIfFrozen(); _names = value; }
        }

        public IList<NameExpression> AsNames {
            get { return _asNames; }
            set { ThrowIfFrozen(); _asNames = value; }
        }

        protected override void OnFreeze() {
            base.OnFreeze();
            _names = FreezeList(_names);
            _asNames = FreezeList(_asNames);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
            }
            walker.PostWalk(this);
        }

        /// <summary>
        /// Removes the import at the specified index (which must be in the range of
        /// the Names property) and returns a new ImportStatement which is the same
        /// as this one minus the imported name.  Preserves all round-tripping metadata
        /// in the process.
        /// 
        /// New in 1.1.
        /// </summary>
        //public ImportStatement RemoveImport(PythonAst ast, int index) {
        //    if (index < 0 || index >= _names.Length) {
        //        throw new ArgumentOutOfRangeException("index");
        //    }
        //    if (ast == null) {
        //        throw new ArgumentNullException("ast");
        //    }

        //    ModuleName[] names = new ModuleName[_names.Length - 1];
        //    NameExpression[] asNames = _asNames == null ? null : new NameExpression[_asNames.Length - 1];
        //    var asNameWhiteSpace = this.GetNamesWhiteSpace(ast);
        //    var itemWhiteSpace = this.GetListWhiteSpace(ast);
        //    List<string> newAsNameWhiteSpace = new List<string>();
        //    List<string> newListWhiteSpace = new List<string>();
        //    int asIndex = 0;
        //    for (int i = 0, write = 0; i < _names.Length; i++) {
        //        bool includingCurrentName = i != index;

        //        // track the white space, this needs to be kept in sync w/ ToCodeString and how the
        //        // parser creates the white space.
        //        if (i > 0 && itemWhiteSpace != null) {
        //            if (includingCurrentName) {
        //                newListWhiteSpace.Add(itemWhiteSpace[i - 1]);
        //            }
        //        }

        //        if (includingCurrentName) {
        //            names[write] = _names[i];

        //            if (_asNames != null) {
        //                asNames[write] = _asNames[i];
        //            }

        //            write++;
        //        }

        //        if (AsNames[i] != null && includingCurrentName) {
        //            if (asNameWhiteSpace != null) {
        //                newAsNameWhiteSpace.Add(asNameWhiteSpace[asIndex++]);
        //            }

        //            if (_asNames[i].Name.Length != 0) {
        //                if (asNameWhiteSpace != null) {
        //                    newAsNameWhiteSpace.Add(asNameWhiteSpace[asIndex++]);
        //                }
        //            }
        //        }
        //    }

        //    var res = new ImportStatement(names, asNames, _forceAbsolute);
        //    ast.CopyAttributes(this, res);
        //    ast.SetAttribute(res, NodeAttributes.NamesWhiteSpace, newAsNameWhiteSpace.ToArray());
        //    ast.SetAttribute(res, NodeAttributes.ListWhiteSpace, newListWhiteSpace.ToArray());

        //    return res;
        //}
    }
}
