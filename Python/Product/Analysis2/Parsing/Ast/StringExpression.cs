﻿// Python Tools for Visual Studio
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
using System.Collections.ObjectModel;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class StringExpression : Expression {
        private IList<Expression> _parts;

        public IList<Expression> Parts {
            get { return _parts; }
            set { ThrowIfFrozen(); _parts = value; }
        }

        protected override void OnFreeze() {
            _parts = FreezeList(_parts);
        }

        public void AddPart(Expression expression) {
            if (_parts == null) {
                _parts = new List<Expression>();
            }
            _parts.Add(expression);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
            }
            walker.PostWalk(this);
        }
    }
}