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

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Native;

namespace Microsoft.PythonTools.DkmDebugger {

    internal class PythonDLLs {
        private static readonly Regex pythonName = new Regex(@"^python(\d\d)(?:_d)?\.dll$");

        public static readonly string[] DebuggerHelperNames = {
            "Microsoft.PythonTools.Debugger.Helper.x86.dll",
            "Microsoft.PythonTools.Debugger.Helper.x64.dll",
        };

        public static readonly string[] CTypesNames = {
            "_ctypes.pyd", "_ctypes_d.pyd"
        };

        private readonly PythonRuntimeInfo _pyrtInfo;
        private DkmNativeModuleInstance _python;

        public PythonDLLs(PythonRuntimeInfo pyrtInfo) {
            _pyrtInfo = pyrtInfo;
        }

        public DkmNativeModuleInstance Python {
            get {
                return _python;
            }
            set {
                _python = value;
                if (value != null) {
                    _pyrtInfo.LanguageVersion = GetPythonLanguageVersion(value);
                    Debug.Assert(_pyrtInfo.LanguageVersion != PythonLanguageVersion.None);
                }
            }
        }

        public DkmNativeModuleInstance DebuggerHelper { get; set; }

        public DkmNativeModuleInstance CTypes { get; set; }

        public static PythonLanguageVersion GetPythonLanguageVersion(DkmNativeModuleInstance moduleInstance) {
            var m = pythonName.Match(moduleInstance.Name);
            if (!m.Success) {
                return PythonLanguageVersion.None;
            }

            var ver = m.Groups[1].Value;
            switch (ver) {
                case "27": return PythonLanguageVersion.V27;
                case "33": return PythonLanguageVersion.V33;
                case "34": return PythonLanguageVersion.V34;
                case "35": return PythonLanguageVersion.V35;
                case "36": return PythonLanguageVersion.V36;
                default: return PythonLanguageVersion.None;
            }
        }
    }

    internal class PythonRuntimeInfo : DkmDataItem {
        public PythonLanguageVersion LanguageVersion { get; set; }

        public PythonDLLs DLLs { get; private set; }

        public PythonRuntimeInfo() {
            DLLs = new PythonDLLs(this);
        }
    }

    internal static class PythonRuntimeInfoExtensions {
        public static PythonRuntimeInfo GetPythonRuntimeInfo(this DkmProcess process) {
            return process.GetOrCreateDataItem(() => new PythonRuntimeInfo());
        }
    }
}
