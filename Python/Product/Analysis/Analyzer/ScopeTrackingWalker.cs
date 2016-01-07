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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    abstract class ScopeTrackingWalker : PythonWalker {
        private readonly Stack<KeyValuePair<string, string>> _scope;

        protected ScopeTrackingWalker() {
            _scope = new Stack<KeyValuePair<string, string>>();
        }

        protected virtual void OnEnterScope() { }

        protected virtual void OnLeaveScope() { }

        protected void EnterScope(string name, string suffix) {
            if (_scope.Count <= 1) {
                _scope.Push(new KeyValuePair<string, string>(name, suffix));
            } else {
                var p = _scope.Peek();
                _scope.Push(new KeyValuePair<string, string>(p.Key + p.Value + name, suffix));
            }
            OnEnterScope();
        }

        protected void LeaveScope(string name, string suffix) {
            var scope = _scope.Pop();
            if (!scope.Key.EndsWith(name)) {
                Debug.Fail("Did not pop " + name + " from " + scope.Key);
                var ex = new InvalidOperationException("Scopes were not processed correctly");
                ex.Data["ExpectedScope"] = name;
                ex.Data["PoppedScope"] = scope.Key;
                ex.Data["PoppedSuffix"] = scope.Value;
                throw ex;
            }
            OnLeaveScope();
        }

        public override bool Walk(PythonAst node) {
            _scope.Clear();

            EnterScope("", "");
            return true;
        }

        public override void PostWalk(PythonAst node) {
            base.PostWalk(node);
            LeaveScope("", "");
        }

        protected string CurrentScope => _scope.Peek().Key;
        protected string CurrentScopeWithSuffix {
            get {
                var p = _scope.Peek();
                return p.Key + p.Value;
            }
        }
        protected IEnumerable<string> CurrentScopes => _scope.Select(p => p.Key);
        protected IEnumerable<string> CurrentScopesWithSuffix => _scope.Select(p => p.Key + p.Value);
    }
}
