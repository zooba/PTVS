﻿// Python Tools for Visual Studio
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
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Intellisense {
    using AP = AnalysisProtocol;

    public sealed class CompletionResult {
        private readonly string _completion;
        private readonly PythonMemberType _memberType;
        private readonly string _name, _doc;
        private readonly AP.CompletionValue[] _values;

        internal CompletionResult(string name, PythonMemberType memberType) {
            _name = name;
            _completion = name;
            _memberType = memberType;
        }

        internal CompletionResult(string name, string completion, string doc, PythonMemberType memberType, AP.CompletionValue[] values) {
            _name = name;
            _memberType = memberType;
            _completion = completion;
            _doc = doc;
            _values = values;
        }

        public string Completion => _completion;
        public string Documentation => _doc;
        public PythonMemberType MemberType => _memberType;
        public string Name => _name;

        internal AP.CompletionValue[] Values => _values ?? Array.Empty<AP.CompletionValue>();
    }
}
