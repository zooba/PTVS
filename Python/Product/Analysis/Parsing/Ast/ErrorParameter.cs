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

using System.Text;

namespace Microsoft.PythonTools.Parsing.Ast {
    class ErrorParameter : Parameter {
        private readonly ErrorExpression _error;
        
        public ErrorParameter(ErrorExpression errorValue)
            : base("", ParameterKind.Normal) {
                _error = errorValue;
        }

        public ErrorExpression Error {
            get {
                return _error;
            }
        }

        internal override void AppendCodeString(StringBuilder res, PythonAst ast, CodeFormattingOptions format, string leadingWhiteSpace) {
            string kwOnlyText = this.GetExtraVerbatimText(ast);
            if (kwOnlyText != null) {
                if (leadingWhiteSpace != null) {
                    res.Append(leadingWhiteSpace);
                    res.Append(kwOnlyText.TrimStart());
                    leadingWhiteSpace = null;
                } else {
                    res.Append(kwOnlyText);
                }
            }
            bool isAltForm = this.IsAltForm(ast);
            if (isAltForm) {
                res.Append(leadingWhiteSpace ?? this.GetPrecedingWhiteSpace(ast));
                res.Append('(');
                leadingWhiteSpace = null;
            }
            _error.AppendCodeString(res, ast, format, leadingWhiteSpace);
            if (this.DefaultValue != null) {
                res.Append(this.GetSecondWhiteSpace(ast));
                res.Append('=');
                this.DefaultValue.AppendCodeString(res, ast, format);
            }
            if (isAltForm && !this.IsMissingCloseGrouping(ast)) {
                res.Append(this.GetSecondWhiteSpace(ast));
                res.Append(')');
            }
        }
    }
}
