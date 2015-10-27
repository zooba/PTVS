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
