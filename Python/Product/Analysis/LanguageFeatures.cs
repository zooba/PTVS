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

using Microsoft.PythonTools.Analysis.Parsing;

namespace Microsoft.PythonTools.Analysis {
    public struct LanguageFeatures {
        private PythonLanguageVersion _version;
        private FutureOptions _future;

        public LanguageFeatures(PythonLanguageVersion version, FutureOptions future) {
            _version = version;
            _future = future;
        }

        public PythonLanguageVersion Version => _version;
        public FutureOptions Future => _future;

        public void AddFuture(FutureOptions future) {
            _future |= future;
        }

        public bool HasAnnotations => _version >= PythonLanguageVersion.V30;
        public bool HasAs => _future.ThrowIfInvalid().HasFlag(FutureOptions.WithStatement) || _version >= PythonLanguageVersion.V26;
        public bool HasAsyncAwait => _version >= PythonLanguageVersion.V35;
        public bool HasBareStarParameter => _version.Is3x();
        public bool HasBytesPrefix => _version >= PythonLanguageVersion.V26;
        public bool HasClassDecorators => _version >= PythonLanguageVersion.V26;
        public bool HasConstantBooleans => _version.Is3x();
        public bool HasDictComprehensions => _version >= PythonLanguageVersion.V27;
        public bool HasExecStatement => _version.Is2x();
        public bool HasGeneralUnpacking => _version >= PythonLanguageVersion.V35;
        public bool HasGeneratorReturn => _version >= PythonLanguageVersion.V33;
        public bool HasLong => _version.Is2x();
        public bool HasNonlocal => _version.Is3x();
        public bool HasOperatorSliceFunctions => _version.Is2x();
        public bool HasPrintFunction => _future.ThrowIfInvalid().HasFlag(FutureOptions.PrintFunction) || _version.Is3x();
        public bool HasRawBytesPrefix => _version >= PythonLanguageVersion.V33;
        public bool HasReprLiterals => _version.Is2x();
        public bool HasSetLiterals => _version >= PythonLanguageVersion.V27;
        public bool HasStarUnpacking => _version.Is3x();
        public bool HasSublistParameters => _version.Is2x();
        public bool HasTrueDivision => _future.ThrowIfInvalid().HasFlag(FutureOptions.TrueDivision) || _version.Is3x();
        public bool HasTupleAsComprehensionTarget => _version.Is2x();
        public bool HasUnicodeLiterals => _future.ThrowIfInvalid().HasFlag(FutureOptions.UnicodeLiterals) || _version.Is3x();
        public bool HasUnicodePrefix => _version < PythonLanguageVersion.V30 || _version >= PythonLanguageVersion.V33;
        public bool HasWith => _future.ThrowIfInvalid().HasFlag(FutureOptions.WithStatement) || _version >= PythonLanguageVersion.V26;
        public bool HasYieldFrom => _version >= PythonLanguageVersion.V33;

        public bool IsUnicodeCalledStr => _version.Is3x();

        public string BuiltinsName => _version.Is3x() ? "builtins" : "__builtin__";
    }
}
