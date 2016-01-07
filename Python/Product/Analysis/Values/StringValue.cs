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

using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing;

namespace Microsoft.PythonTools.Analysis.Values {
    class StringValue : InstanceValue {
        private string _strValue;
        private ByteString _bytesValue;

        public StringValue(VariableKey key, StringType type) : base(key, type.Key) { }

        public StringValue(VariableKey key, StringType type, string value) : base(key, type.Key) {
            _strValue = value;
        }

        public StringValue(VariableKey key, StringType type, ByteString value) : base(key, type.Key) {
            _bytesValue = value;
        }

        public string TextValue => _strValue;
        public ByteString BytesValue => _bytesValue;

        public string AsString() => _strValue ?? _bytesValue?.String;

        public override async Task<string> ToDebugAnnotationAsync(CancellationToken cancellationToken) {
            var str = AsString();
            return string.IsNullOrEmpty(str) ?
                await base.ToDebugAnnotationAsync(cancellationToken) :
                string.Format("{0}(\"{1}\")", await base.ToDebugAnnotationAsync(cancellationToken), str);
        }
    }
}
