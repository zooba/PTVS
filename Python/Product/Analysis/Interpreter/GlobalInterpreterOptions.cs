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

using System.ComponentModel.Composition;

namespace Microsoft.PythonTools.Interpreter {
    /// <summary>
    /// Allows global (process-wide) options to be set for all interpreters.
    /// 
    /// This is intended primarily for the analyzer process. Most code should
    /// never set these options and should only read them.
    /// </summary>
    public static class GlobalInterpreterOptions {
        /// <summary>
        /// When True, factories should not watch the file system.
        /// </summary>
        public static bool SuppressFileSystemWatchers { get; set; }

        /// <summary>
        /// When True, factories should never provide a package manager.
        /// </summary>
        public static bool SuppressPackageManagers { get; set; }
    }
}
