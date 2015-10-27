using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Parsing {
    public struct Token2 {
        public static readonly Token2 Empty = new Token2();
        public static readonly Token2 EOF = new Token2(TokenCategory.EndOfStream);

        public TokenCategory Category;
        public SourceSpan Span;

        public Token2(TokenCategory category) {
            Category = category;
            Span = SourceSpan.None;
        }

        public Token2(TokenCategory category, SourceLocation start, SourceLocation end) {
            Category = category;
            Span = new SourceSpan(start, end);
        }

        public Token2(TokenCategory category, SourceLocation start, int length) {
            Category = category;
            Span = new SourceSpan(start, new SourceLocation(start.Index + length, start.Line, start.Column + length));
        }
    }
}
