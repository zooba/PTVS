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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class StringExpression : Expression {
        private IList<Expression> _parts;

        public IList<Expression> Parts {
            get { return _parts; }
            set { ThrowIfFrozen(); _parts = value; }
        }

        private static bool IsNotStringPart(Expression part) {
            var ce = part as ConstantExpression;
            return !(ce?.Value is string);
        }

        internal string ToSimpleString() {
            if (_parts == null || _parts.Any(IsNotStringPart)) {
                return null;
            }
            return string.Join("", _parts.OfType<ConstantExpression>().Select(p => (string)p.Value));
        }

        private static bool IsNotByteStringPart(Expression part) {
            var ce = part as ConstantExpression;
            return !(ce?.Value is AsciiString);
        }

        private static IEnumerable<byte> Identity(IReadOnlyList<byte> source) {
            return source;
        }

        internal IReadOnlyList<byte> ToSimpleByteString() {
            if (_parts == null || _parts.Any(IsNotByteStringPart)) {
                return null;
            }
            return _parts
                .Select(p => ((AsciiString)((ConstantExpression)p).Value).Bytes)
                .SelectMany(Identity)
                .ToArray();
        }

        public string GetConstantRepr(PythonLanguageVersion version) {
            if (_parts == null) {
                return string.Empty;
            }
            var res = new StringBuilder();
            foreach (var p in _parts) {
                var ce = p as ConstantExpression;
                if (ce != null) {
                    res.Append(ce.GetConstantRepr(version));
                    res.Append(' ');
                } else {
                    Debug.Fail("Unsupported part type");
                }
            }
            if (res.Length > 0) {
                res.Length -= 1;
            }
            return res.ToString();
        }

        protected override void OnFreeze() {
            _parts = FreezeList(_parts);
        }

        public void AddPart(Expression expression) {
            if (_parts == null) {
                _parts = new List<Expression>();
            }
            _parts.Add(expression);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => "literal";
    }
}
