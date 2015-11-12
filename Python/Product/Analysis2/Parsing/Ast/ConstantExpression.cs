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
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class ConstantExpression : Expression {
        private object _value;

        public ConstantExpression() { }

        public object Value {
            get { return _value; }
            set { ThrowIfFrozen(); _value = value; }
        }

        internal override string CheckName => "literal";

        internal override void CheckAssign(Parser parser) {
            if (_value == null) {
                parser.ReportError("assignment to None", Span);
                return;
            }

            base.CheckAssign(parser);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
            }
            walker.PostWalk(this);
        }

        private static bool IsNegativeZero(double value) {
            return (value == 0.0) && double.IsNegativeInfinity(1.0 / value);
        }

        // ToString does not distinguish between negative zero and positive zero, but repr() does, and so should we.
        private static string NegativeZeroAwareToString(double n) {
            return IsNegativeZero(n) ? "-0" : n.ToString("g", nfi);
        }

        private void AppendEscapedString(StringBuilder res, string s, bool escape8bitStrings) {
            char quote = s.Contains("'") && !s.Contains("\"") ? '"' : '\'';
            res.Append(quote);
            foreach (var c in s) {
                switch (c) {
                    case '\n': res.Append("\\n"); break;
                    case '\r': res.Append("\\r"); break;
                    case '\t': res.Append("\\t"); break;
                    case '\'': res.Append(quote == c ? "\\'" : "'"); break;
                    case '\\': res.Append("\\\\"); break;
                    default:
                        ushort cp = (ushort)c;
                        if (cp > 0xFF) {
                            res.AppendFormat("\\u{0:x04}", cp);
                        } else if (cp < 0x20 || (escape8bitStrings && cp >= 0x7F)) {
                            res.AppendFormat("\\x{0:x02}", cp);
                        } else {
                            res.Append(c);
                        }
                        break;
                }
            }
            res.Append(quote);
        }

        public string GetConstantRepr(PythonLanguageVersion version, bool escape8bitStrings = false) {
            if (_value == null) {
                return "None";
            } else if (_value is AsciiString) {
                StringBuilder res = new StringBuilder();
                if (!version.Is2x()) {
                    res.Append("b");
                }
                AppendEscapedString(res, ((AsciiString)_value).String, escape8bitStrings);
                return res.ToString();
            } else if (_value is string) {
                StringBuilder res = new StringBuilder();
                if (!version.Is3x()) {
                    res.Append("u");
                }
                AppendEscapedString(res, (string)_value, escape8bitStrings);
                return res.ToString();
            } else if (_value is Complex) {
                Complex n = (Complex)_value;
                string real = NegativeZeroAwareToString(n.Real);
                string imag =  NegativeZeroAwareToString(n.Imaginary);
                if (n.Real != 0) {
                    if (!imag.StartsWith("-")) {
                        imag = "+" + imag;
                    }
                    return "(" + real + imag + "j)";
                } else {
                    return imag + "j";
                }
            } else if (_value is BigInteger) {
                if (!version.Is3x()) {
                    return _value.ToString() + "L";
                }
            } else if (_value is double) {
                double n = (double)_value;
                string s = NegativeZeroAwareToString(n);
                // If there's no fractional part, and this is not NaN or +-Inf, G format will not include the decimal
                // point. This is okay if we're using scientific notation as this implies float, but if not, add the
                // decimal point to indicate the type, just like Python repr() does.
                if ((n - Math.Truncate(n)) == 0 && !s.Contains("e")) {
                    s += ".0";
                }
                return s;
            } else if (_value is IFormattable) {
                return ((IFormattable)_value).ToString(null, CultureInfo.InvariantCulture);
            }

            // TODO: We probably need to handle more primitives here
            return _value.ToString();
        }

        private static NumberFormatInfo _nfi;

        private static NumberFormatInfo nfi {
            get {
                if (_nfi == null) {
                    NumberFormatInfo numberFormatInfo = ((CultureInfo)CultureInfo.InvariantCulture.Clone()).NumberFormat;
                    // The CLI formats as "Infinity", but CPython formats differently
                    numberFormatInfo.PositiveInfinitySymbol = "inf";
                    numberFormatInfo.NegativeInfinitySymbol = "-inf";
                    numberFormatInfo.NaNSymbol = "nan";
                    numberFormatInfo.NumberDecimalDigits = 0;
                    _nfi = numberFormatInfo;
                }
                return _nfi;
            }
        }
    }
}
