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

using System.Linq;

namespace Microsoft.PythonTools.Analysis.Parsing {
    public enum TokenUsage {
        None = 0x00 << 8,
        Primary = 0x01 << 8,
        BeginStatement = 0x02 << 8,
        EndStatement = 0x03 << 8,
        BinaryOperator = 0x04 << 8,
        UnaryOperator = 0x05 << 8,
        BinaryOrUnaryOperator = 0x06 << 8,
        Assignment = 0x07 << 8,
        Comparison = 0x08 << 8,
        BeginGroup = 0x09 << 8,
        EndGroup = 0x10 << 8,
        BeginStatementOrBinaryOperator = 0x11 << 8,

        Mask = 0xFF << 8
    }

    public enum TokenCategory {
        None = 0x00 << 16,
        Identifier = 0x01 << 16,
        Keyword = 0x02 << 16,
        Operator = 0x03 << 16,
        StringLiteral = 0x04 << 16,
        NumericLiteral = 0x05 << 16,
        Whitespace = 0x06 << 16,
        Comment = 0x07 << 16,
        Grouping = 0x08 << 16,
        Delimiter = 0x09 << 16,

        Mask = 0xFF << 16
    }

    public enum TokenKind {
        Unknown = 0,
        Mask = 0xFF,

        EndOfFile = 0x01 | TokenUsage.EndStatement,
        Error = 0x02,
        NewLine = 0x03 | TokenUsage.EndStatement | TokenCategory.Whitespace,
        Whitespace = 0x04 | TokenCategory.Whitespace,
        SignificantWhitespace = 0x04 | TokenUsage.BeginStatement | TokenCategory.Whitespace,
        Comment = 0x05 | TokenCategory.Comment,
        Name = 0x06 | TokenUsage.Primary | TokenCategory.Identifier,
        Ellipsis = 0x07 | TokenUsage.Primary | TokenCategory.Identifier,
        Arrow = 0x08 | TokenCategory.Operator,
        Dot = 0x09 | TokenCategory.Identifier,
        ExplicitLineJoin = 0x0A | TokenUsage.None | TokenCategory.Operator,

        Add = 0x10 | TokenUsage.BinaryOrUnaryOperator | TokenCategory.Operator,
        AddEqual = 0x10 | TokenUsage.Assignment | TokenCategory.Operator,
        Subtract = 0x11 | TokenUsage.BinaryOrUnaryOperator | TokenCategory.Operator,
        SubtractEqual = 0x11 | TokenUsage.Assignment | TokenCategory.Operator,
        Power = 0x12 | TokenCategory.Operator,
        PowerEqual = 0x12 | TokenUsage.Assignment | TokenCategory.Operator,
        Multiply = 0x13 | TokenUsage.BinaryOperator | TokenCategory.Operator,
        MultiplyEqual = 0x13 | TokenUsage.Assignment | TokenCategory.Operator,
        MatMultiply = 0x14 | TokenUsage.BinaryOperator | TokenCategory.Operator,
        MatMultiplyEqual = 0x14 | TokenUsage.Assignment | TokenCategory.Operator,
        At = 0x14 | TokenUsage.BeginStatement | TokenCategory.Identifier,
        FloorDivide = 0x15 | TokenUsage.BinaryOperator | TokenCategory.Operator,
        FloorDivideEqual = 0x15 | TokenUsage.Assignment | TokenCategory.Operator,
        Divide = 0x16 | TokenUsage.BinaryOperator | TokenCategory.Operator,
        DivideEqual = 0x16 | TokenUsage.Assignment | TokenCategory.Operator,
        Mod = 0x17 | TokenUsage.BinaryOperator | TokenCategory.Operator,
        ModEqual = 0x17 | TokenUsage.Assignment | TokenCategory.Operator,
        LeftShift = 0x18 | TokenUsage.BinaryOperator | TokenCategory.Operator,
        LeftShiftEqual = 0x18 | TokenUsage.Assignment | TokenCategory.Operator,
        RightShift = 0x19 | TokenUsage.BinaryOperator | TokenCategory.Operator,
        RightShiftEqual = 0x19 | TokenUsage.Assignment | TokenCategory.Operator,
        BitwiseAnd = 0x1A | TokenUsage.BinaryOperator | TokenCategory.Operator,
        BitwiseAndEqual = 0x1A | TokenUsage.Assignment | TokenCategory.Operator,
        BitwiseOr = 0x1B | TokenUsage.BinaryOperator | TokenCategory.Operator,
        BitwiseOrEqual = 0x1B | TokenUsage.Assignment | TokenCategory.Operator,
        ExclusiveOr = 0x1C | TokenUsage.BinaryOperator | TokenCategory.Operator,
        ExclusiveOrEqual = 0x1C | TokenUsage.Assignment | TokenCategory.Operator,
        Twiddle = 0x1D | TokenUsage.UnaryOperator | TokenCategory.Operator,

        LessThan = 0x20 | TokenUsage.Comparison | TokenCategory.Operator,
        GreaterThan = 0x21 | TokenUsage.Comparison | TokenCategory.Operator,
        LessThanOrEqual = 0x22 | TokenUsage.Comparison | TokenCategory.Operator,
        GreaterThanOrEqual = 0x23 | TokenUsage.Comparison | TokenCategory.Operator,
        Equals = 0x24 | TokenUsage.Comparison | TokenCategory.Operator,
        NotEquals = 0x25 | TokenUsage.Comparison | TokenCategory.Operator,
        LessThanGreaterThan = 0x26 | TokenUsage.Comparison | TokenCategory.Operator,

        LeftParenthesis = 0x30 | TokenUsage.BeginGroup | TokenCategory.Grouping,
        RightParenthesis = 0x30 | TokenUsage.EndGroup | TokenCategory.Grouping,
        LeftBracket = 0x31 | TokenUsage.BeginGroup | TokenCategory.Grouping,
        RightBracket = 0x31 | TokenUsage.EndGroup | TokenCategory.Grouping,
        LeftBrace = 0x32 | TokenUsage.BeginGroup | TokenCategory.Grouping,
        RightBrace = 0x32 | TokenUsage.EndGroup | TokenCategory.Grouping,
        LeftSingleQuote = 0x33 | TokenUsage.BeginGroup | TokenCategory.StringLiteral,
        RightSingleQuote = 0x33 | TokenUsage.EndGroup | TokenCategory.StringLiteral,
        LeftSingleTripleQuote = 0x34 | TokenUsage.BeginGroup | TokenCategory.StringLiteral,
        RightSingleTripleQuote = 0x34 | TokenUsage.EndGroup | TokenCategory.StringLiteral,
        LeftDoubleQuote = 0x35 | TokenUsage.BeginGroup | TokenCategory.StringLiteral,
        RightDoubleQuote = 0x35 | TokenUsage.EndGroup | TokenCategory.StringLiteral,
        LeftDoubleTripleQuote = 0x36 | TokenUsage.BeginGroup | TokenCategory.StringLiteral,
        RightDoubleTripleQuote = 0x36 | TokenUsage.EndGroup | TokenCategory.StringLiteral,
        LeftBackQuote = 0x37 | TokenUsage.BeginGroup | TokenCategory.Grouping,
        RightBackQuote = 0x37 | TokenUsage.EndGroup | TokenCategory.Grouping,

        Comma = 0x38 | TokenUsage.None | TokenCategory.Delimiter,
        Colon = 0x39 | TokenUsage.None | TokenCategory.Delimiter,
        SemiColon = 0x3A | TokenUsage.EndStatement | TokenCategory.Delimiter,
        Assign = 0x3B | TokenUsage.Assignment | TokenCategory.Operator,

        KeywordAnd = 0x40 | TokenUsage.BinaryOperator | TokenCategory.Keyword,
        KeywordAssert = 0x41 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordAsync = 0x42 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordAwait = 0x43 | TokenUsage.None | TokenCategory.Keyword,
        KeywordBreak = 0x44 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordClass = 0x45 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordContinue = 0x46 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordDef = 0x47 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordDel = 0x48 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordElseIf = 0x49 | TokenUsage.None | TokenCategory.Keyword,
        KeywordElse = 0x4A | TokenUsage.None | TokenCategory.Keyword,
        KeywordExcept = 0x4B | TokenUsage.None | TokenCategory.Keyword,
        KeywordExec = 0x4C | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordFinally = 0x4D | TokenUsage.None | TokenCategory.Keyword,
        KeywordFor = 0x4E | TokenUsage.BeginStatementOrBinaryOperator | TokenCategory.Keyword,
        KeywordFrom = 0x4F | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordGlobal = 0x50 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordIf = 0x51 | TokenUsage.BeginStatementOrBinaryOperator | TokenCategory.Keyword,
        KeywordImport = 0x52 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordIn = 0x53 | TokenUsage.Comparison | TokenCategory.Keyword,
        KeywordIs = 0x54 | TokenUsage.Comparison | TokenCategory.Keyword,
        KeywordLambda = 0x55 | TokenUsage.None | TokenCategory.Keyword,
        KeywordNot = 0x56 | TokenUsage.UnaryOperator | TokenCategory.Keyword,
        KeywordOr = 0x57 | TokenUsage.BinaryOperator | TokenCategory.Keyword,
        KeywordPass = 0x58 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordPrint = 0x59 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordRaise = 0x5A | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordReturn = 0x5B | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordTry = 0x5C | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordWhile = 0x5D | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordYield = 0x5E | TokenUsage.None | TokenCategory.Keyword,
        KeywordAs = 0x5F | TokenUsage.None | TokenCategory.Keyword,
        KeywordWith = 0x60 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordTrue = 0x61 | TokenUsage.Primary | TokenCategory.Keyword,
        KeywordFalse = 0x62 | TokenUsage.Primary | TokenCategory.Keyword,
        KeywordNonlocal = 0x63 | TokenUsage.BeginStatement | TokenCategory.Keyword,
        KeywordNone = 0x64 | TokenUsage.Primary | TokenCategory.Keyword,

        LiteralString = 0x70 | TokenUsage.None | TokenCategory.StringLiteral,
        LiteralBinary = 0x71 | TokenUsage.Primary | TokenCategory.NumericLiteral,
        LiteralDecimal = 0x72 | TokenUsage.Primary | TokenCategory.NumericLiteral,
        LiteralHex = 0x73 | TokenUsage.Primary | TokenCategory.NumericLiteral,
        LiteralOctal = 0x74 | TokenUsage.Primary | TokenCategory.NumericLiteral,
        LiteralFloat = 0x75 | TokenUsage.Primary | TokenCategory.NumericLiteral,
        LiteralImaginary = 0x76 | TokenUsage.Primary | TokenCategory.NumericLiteral,
        LiteralDecimalLong = 0x77 | TokenUsage.Primary | TokenCategory.NumericLiteral,
        LiteralHexLong = 0x78 | TokenUsage.Primary | TokenCategory.NumericLiteral,
        LiteralOctalLong = 0x79 | TokenUsage.Primary | TokenCategory.NumericLiteral
    }

    public static class TokenKindExtensions {
        public static TokenCategory GetCategory(this TokenKind kind) {
            return (TokenCategory)((int)kind & (int)TokenCategory.Mask);
        }

        public static TokenUsage GetUsage(this TokenKind kind) {
            return (TokenUsage)((int)kind & (int)TokenUsage.Mask);
        }

        public static TokenKind GetGroupEnding(this TokenKind kind) {
            if (kind.GetUsage() != TokenUsage.BeginGroup) {
                return TokenKind.Unknown;
            }

            return (TokenKind)(((int)kind & ~(int)TokenUsage.Mask) | (int)TokenUsage.EndGroup);
        }

        public static bool Is(this Token token, TokenKind kind) {
            return token.Kind == kind;
        }

        public static bool IsAny(this Token token, TokenKind kind1, TokenKind kind2) {
            var k = token.Kind;
            return k == kind1 || k == kind2;
        }

        public static bool IsAny(this Token token, TokenUsage use1, TokenUsage use2) {
            var u = token.Kind.GetUsage();
            return u == use1 || u == use2;
        }

        public static bool IsAny(this Token token, params TokenKind[] kinds) {
            return kinds.Contains(token.Kind);
        }

        public static bool Is(this Token token, TokenUsage usage) {
            return token.Kind.GetUsage() == usage;
        }

        public static bool Is(this Token token, TokenCategory category) {
            return token.Kind.GetCategory() == category;
        }
    }
}
