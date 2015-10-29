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
using System.Diagnostics;

namespace Microsoft.PythonTools.Analysis.Parsing {
    [DebuggerDisplay("{Kind} ({Span})")]
    public struct Token : IEquatable<Token> {
        public static readonly Token Empty = new Token();

        public TokenKind Kind;
        public SourceSpan Span;

        public Token(TokenKind kind, SourceLocation start, SourceLocation end) {
            Kind = kind;
            Span = new SourceSpan(start, end);
        }

        public Token(TokenKind kind, SourceLocation start, int length) {
            Kind = kind;
            Span = new SourceSpan(start, new SourceLocation(start.Index + length, start.Line, start.Column + length));
        }

        public override bool Equals(object obj) {
            if (!(obj is Token)) {
                return false;
            }
            return Equals((Token)obj);
        }

        public bool Equals(Token other) {
            return Kind == other.Kind && Span == other.Span;
        }

        public override int GetHashCode() {
            return Span.GetHashCode();
        }
    }
}
