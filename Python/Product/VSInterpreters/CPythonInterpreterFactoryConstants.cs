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

namespace Microsoft.PythonTools.Interpreter {
    /// <summary>
    /// Provides constants used to identify interpreters that are detected from
    /// the CPython registry settings.
    /// 
    /// This class used by Microsoft.PythonTools.dll to register the
    /// interpreters.
    /// </summary>
    public static class CPythonInterpreterFactoryConstants {
        public const string Id32 = "{2AF0F10D-7135-4994-9156-5D01C9C11B7E}";
        public const string Id64 = "{9A7A9026-48C1-4688-9D5D-E5699D47D074}";

        public static readonly Guid Guid32 = new Guid(Id32);
        public static readonly Guid Guid64 = new Guid(Id64);

        public const string ConsoleExecutable = "python.exe";
        public const string WindowsExecutable = "pythonw.exe";
        public const string LibrarySubPath = "lib";
        public const string PathEnvironmentVariableName = "PYTHONPATH";

        public const string Description32 = "Python";
        public const string Description64 = "Python 64-bit";
    }
}
