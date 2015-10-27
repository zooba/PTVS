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

using System.Collections.Generic;
using Microsoft.PythonTools.Analysis.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Parsing {
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
