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

    public enum TokenCategory {
        None,

        /// <summary>
        /// A token marking an end of stream.
        /// </summary>
        EndOfStream,

        /// <summary>
        /// A token marking the end of a line
        /// </summary>
        EndOfLine,

        /// <summary>
        /// A token indicating that the following end of line should be ignored
        /// </summary>
        IgnoreEndOfLine,

        /// <summary>
        /// A space, tab, or newline.
        /// </summary>
        WhiteSpace,

        /// <summary>
        /// A block comment.
        /// </summary>
        Comment,

        /// <summary>
        /// A decimal integer literal.
        /// </summary>
        DecimalIntegerLiteral,

        /// <summary>
        /// An octal integer literal (0o[0-7]+)
        /// </summary>
        OctalIntegerLiteral,

        /// <summary>
        /// A hexadecimal integer literal (0x[0-9A-Fa-f]+)
        /// </summary>
        HexadecimalIntegerLiteral,

        /// <summary>
        /// A binary integer literal (0b[01]+)
        /// </summary>
        BinaryIntegerLiteral,

        /// <summary>
        /// A floating-point literal
        /// </summary>
        FloatingPointLiteral,

        /// <summary>
        /// An imaginary floating-point literal
        /// </summary>
        ImaginaryLiteral,

        /// <summary>
        /// A string literal.
        /// </summary>
        StringLiteral,

        /// <summary>
        /// A punctuation character that has a specific meaning in a language.
        /// </summary>
        Operator,

        /// <summary>
        /// A token that operates as a separator between two language elements.
        /// </summary>
        Comma,

        /// <summary>
        /// A token that operates as a separator between two names.
        /// </summary>
        Period,

        /// <summary>
        /// A token that operates as a separator between two language elements.
        /// </summary>
        SemiColon,

        /// <summary>
        /// A token that operates as a separator following a label.
        /// </summary>
        Colon,

        /// <summary>
        /// An identifier
        /// </summary>
        Identifier,

        /// <summary>
        /// Opening brace, parenthesis or bracket.
        /// </summary>
        OpenGrouping,

        /// <summary>
        /// Closing brace, parenthesis or bracket.
        /// </summary>
        CloseGrouping,

        /// <summary>
        /// Opening single, double or triple quote.
        /// </summary>
        OpenQuote,

        /// <summary>
        /// Closing single, double or triple quote.
        /// </summary>
        CloseQuote,

        /// <summary>
        /// Errors.
        /// </summary>
        Error
    }
}