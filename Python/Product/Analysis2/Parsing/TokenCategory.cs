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