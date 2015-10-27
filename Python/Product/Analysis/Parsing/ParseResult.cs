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
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Parsing {
    public sealed class ParseResult {
        private readonly PythonAst _tree;
        private readonly ParseState _state;
        private readonly IReadOnlyCollection<ErrorResult> _errors;

        internal ParseResult(PythonAst tree, ParseState state, IReadOnlyCollection<ErrorResult> errors) {
            _tree = tree;
            _state = state;
            _errors = errors;
        }

        public PythonAst Tree {
            get { return _tree; }
        }

        public ParseState State {
            get { return _state; }
        }

        public IReadOnlyCollection<ErrorResult> Errors {
            get { return _errors; }
        }
    }

    public enum ParseState {
        /// <summary>
        /// Source code is a syntactically correct.
        /// </summary>
        Complete,

        /// <summary>
        /// Source code represents an empty statement/expression.
        /// </summary>
        Empty,
            
        /// <summary>
        /// Source code is already invalid and no suffix can make it syntactically correct.
        /// </summary>
        Invalid,

        /// <summary>
        /// Last token is incomplete. Source code can still be completed correctly.
        /// </summary>
        IncompleteToken,

        /// <summary>
        /// Last statement is incomplete. Source code can still be completed correctly.
        /// </summary>
        IncompleteStatement,
    }
}
