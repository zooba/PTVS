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
    public enum PythonOperator {
        None,

        // Unary
        Not,
        Pos,
        Invert,
        Negate,

        // Binary

        Add,
        Subtract,
        Multiply,
        MatMultiply,
        Divide,
        TrueDivide,
        Mod,
        BitwiseAnd,
        BitwiseOr,
        BitwiseXor,
        LeftShift,
        RightShift,
        Power,
        FloorDivide,
        And,
        Or,

        // Comparisons

        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        Equal,
        NotEqual,
        In,
        NotIn,
        IsNot,
        Is,
    }

    internal static class PythonOperatorExtensions {
        internal static string ToCodeString(this PythonOperator self) {
            switch (self) {
                case PythonOperator.Not: return "not";
                case PythonOperator.Pos: return "+";
                case PythonOperator.Invert: return "~";
                case PythonOperator.Negate: return "-";
                case PythonOperator.Add: return "+";
                case PythonOperator.Subtract: return "-";
                case PythonOperator.Multiply: return "*";
                case PythonOperator.MatMultiply: return "@";
                case PythonOperator.Divide: return "/";
                case PythonOperator.TrueDivide: return "/";
                case PythonOperator.Mod: return "%";
                case PythonOperator.BitwiseAnd: return "&";
                case PythonOperator.BitwiseOr: return "|";
                case PythonOperator.BitwiseXor: return "^";
                case PythonOperator.LeftShift: return "<<";
                case PythonOperator.RightShift: return ">>";
                case PythonOperator.Power: return "**";
                case PythonOperator.FloorDivide: return "//";
                case PythonOperator.LessThan: return "<";
                case PythonOperator.LessThanOrEqual: return "<=";
                case PythonOperator.GreaterThan: return ">";
                case PythonOperator.GreaterThanOrEqual: return ">=";
                case PythonOperator.Equal: return "==";
                case PythonOperator.NotEqual: return "!=";
                case PythonOperator.In: return "in";
                case PythonOperator.NotIn: return "not in";
                case PythonOperator.IsNot: return "is not";
                case PythonOperator.Is: return "is";
            }
            return "";
        }

        public static PythonOperator GetBinaryOperator(this TokenKind kind) {
            switch (kind) {
                case TokenKind.Add:
                case TokenKind.AddEqual:
                    return PythonOperator.Add;
                case TokenKind.Subtract:
                case TokenKind.SubtractEqual:
                    return PythonOperator.Subtract;
                case TokenKind.Multiply:
                case TokenKind.MultiplyEqual:
                    return PythonOperator.Multiply;
                case TokenKind.MatMultiply:
                case TokenKind.MatMultiplyEqual:
                    return PythonOperator.MatMultiply;
                case TokenKind.Divide:
                case TokenKind.DivideEqual:
                    return PythonOperator.Divide;
                case TokenKind.Mod:
                case TokenKind.ModEqual:
                    return PythonOperator.Mod;
                case TokenKind.BitwiseAnd:
                case TokenKind.BitwiseAndEqual:
                    return PythonOperator.BitwiseAnd;
                case TokenKind.BitwiseOr:
                case TokenKind.BitwiseOrEqual:
                    return PythonOperator.BitwiseOr;
                case TokenKind.ExclusiveOr:
                case TokenKind.ExclusiveOrEqual:
                    return PythonOperator.BitwiseXor;
                case TokenKind.LeftShift:
                case TokenKind.LeftShiftEqual:
                    return PythonOperator.LeftShift;
                case TokenKind.RightShift:
                case TokenKind.RightShiftEqual:
                    return PythonOperator.RightShift;
                case TokenKind.Power:
                case TokenKind.PowerEqual:
                    return PythonOperator.Power;
                case TokenKind.FloorDivide:
                case TokenKind.FloorDivideEqual:
                    return PythonOperator.FloorDivide;
                case TokenKind.LessThan: return PythonOperator.LessThan;
                case TokenKind.LessThanOrEqual: return PythonOperator.LessThanOrEqual;
                case TokenKind.GreaterThan: return PythonOperator.GreaterThan;
                case TokenKind.GreaterThanOrEqual: return PythonOperator.GreaterThanOrEqual;
                case TokenKind.LessThanGreaterThan: return PythonOperator.NotEqual;
                case TokenKind.Equals: return PythonOperator.Equal;
                case TokenKind.NotEquals: return PythonOperator.NotEqual;
                case TokenKind.KeywordIn: return PythonOperator.In;
                case TokenKind.KeywordIs: return PythonOperator.Is;
            }
            return PythonOperator.None;
        }

        public static int GetPrecedence(this PythonOperator op) {
            switch (op) {
                case PythonOperator.Is:
                case PythonOperator.IsNot:
                    return 10;
                case PythonOperator.In:
                case PythonOperator.NotIn:
                    return 11;
                case PythonOperator.BitwiseOr:
                    return 15;
                case PythonOperator.BitwiseXor:
                    return 16;
                case PythonOperator.BitwiseAnd:
                    return 17;
                case PythonOperator.LeftShift:
                case PythonOperator.RightShift:
                    return 18;
                case PythonOperator.Add:
                case PythonOperator.Subtract:
                    return 20;
                case PythonOperator.Multiply:
                case PythonOperator.MatMultiply:
                case PythonOperator.Divide:
                case PythonOperator.TrueDivide:
                case PythonOperator.FloorDivide:
                case PythonOperator.Mod:
                    return 25;
                case PythonOperator.Power:
                    return 30;
            }
            return -1;
        }
    }
}
