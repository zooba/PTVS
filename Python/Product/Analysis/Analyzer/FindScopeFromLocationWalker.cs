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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    class FindScopeFromLocationWalker : ScopeTrackingWalker {
        private readonly SourceLocation _location;
        private readonly string _name;

        private Node _foundNode;
        private IReadOnlyCollection<string> _foundScopes;

        private FindScopeFromLocationWalker(SourceLocation location, string name) {
            _location = location;
            _name = name;
        }

        public static Node FindNode(PythonAst ast, SourceLocation location) {
            var walker = new FindScopeFromLocationWalker(location, null);
            ast.Walk(walker);
            return walker._foundNode;
        }

        public static IReadOnlyCollection<string> FindScopeNames(PythonAst ast, SourceLocation location) {
            var walker = new FindScopeFromLocationWalker(location, null);
            ast.Walk(walker);
            return walker._foundScopes;
        }

        public static IReadOnlyCollection<string> FindNames(PythonAst ast, SourceLocation location, string name) {
            var walker = new FindScopeFromLocationWalker(location, name);
            ast.Walk(walker);
            return walker._foundScopes;
        }

        public override bool Walk(SuiteStatement node) {
            var start = node.Span.Start;
            var end = node.Span.End;
            if (start.Line > _location.Line || end.Line < _location.Line) {
                return false;
            }
            if (_location.Column < node.Indent.End.Column) {
                return false;
            }

            _foundNode = node;
            if (string.IsNullOrEmpty(_name)) {
                _foundScopes = CurrentScopes.ToArray();
            } else {
                _foundScopes = CurrentScopesWithSuffix.Select(s => s + _name).ToArray();
            }
            return true;
        }
    }
}
