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


namespace Microsoft.PythonTools.Analysis.Parsing {

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Dedent")]
    public enum TokenKind {
        EndOfFile = -1,
        Unknown,
        NewLine,
        Whitespace,
        Indent,
        Dedent,
        Comment,
        Error,
        Name,
        Constant,
        Ellipsis,
        Arrow,
        Dot,


        Add,
        AddEqual,
        Subtract,
        SubtractEqual,
        Power,
        PowerEqual,
        Multiply,
        MultiplyEqual,
        MatMultiply,
        MatMultiplyEqual,
        FloorDivide,
        FloorDivideEqual,
        Divide,
        DivideEqual,
        Mod,
        ModEqual,
        LeftShift,
        LeftShiftEqual,
        RightShift,
        RightShiftEqual,
        BitwiseAnd,
        BitwiseAndEqual,
        BitwiseOr,
        BitwiseOrEqual,
        ExclusiveOr,
        ExclusiveOrEqual,
        LessThan,
        GreaterThan,
        LessThanOrEqual,
        GreaterThanOrEqual,
        Equals,
        NotEquals,
        LessThanGreaterThan,
        LeftParenthesis,
        RightParenthesis,
        LeftBracket,
        RightBracket,
        LeftBrace,
        RightBrace,
        Comma,
        Colon,
        BackQuote,
        Semicolon,
        Assign,
        Twiddle,
        At,
        LeftQuote,
        RightQuote,

        KeywordAnd,
        KeywordAssert,
        KeywordAsync,
        KeywordAwait,
        KeywordBreak,
        KeywordClass,
        KeywordContinue,
        KeywordDef,
        KeywordDel,
        KeywordElseIf,
        KeywordElse,
        KeywordExcept,
        KeywordExec,
        KeywordFinally,
        KeywordFor,
        KeywordFrom,
        KeywordGlobal,
        KeywordIf,
        KeywordImport,
        KeywordIn,
        KeywordIs,
        KeywordLambda,
        KeywordNot,
        KeywordOr,
        KeywordPass,
        KeywordPrint,
        KeywordRaise,
        KeywordReturn,
        KeywordTry,
        KeywordWhile,
        KeywordYield,
        KeywordAs,
        KeywordWith,
        KeywordTrue,
        KeywordFalse,
        KeywordNonlocal,

        ExplicitLineJoin
    }
}
