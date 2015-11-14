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

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class DottedName : Node {
        private IList<NameExpression> _names;

        public IList<NameExpression> Names {
            get { return _names; }
            set { ThrowIfFrozen(); _names = value; }
        }

        protected override void OnFreeze() {
            base.OnFreeze();
            _names = FreezeList(_names);
        }

        public virtual string MakeString() {
            if (_names == null || _names.Count == 0) {
                return String.Empty;
            }

            return string.Join(".", _names.Select(n => n?.Name ?? ""));
        }

        public bool IsFuture => Names?.Count == 1 && Names[0].Name == "__future__";

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
            }
            walker.PostWalk(this);
        }

    }
}
