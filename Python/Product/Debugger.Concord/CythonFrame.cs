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
// MERCHANTABILITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System.Linq;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Native;

namespace Microsoft.PythonTools.Debugger.Concord {
    static class CythonFrame {
        public static DkmStackWalkFrame TryCreate(DkmStackContext stackContext, DkmStackWalkFrame frame) {
            var loc = frame?.BasicSymbolInfo?.SourcePosition;
            if (loc == null) {
                return null;
            }
            if (!(loc.DocumentName ?? "").EndsWithOrdinal(".pyx", ignoreCase: true)) {
                return null;
            }

            var pyx = new CythonExpressionEvaluator(frame);

            var nativeAddr = frame.InstructionAddress as DkmNativeInstructionAddress;
            if (nativeAddr == null) {
                return null;
            }

            var pythonRuntime = frame.Process.GetPythonRuntimeInstance();
            var mods = pythonRuntime.GetModuleInstances()
                .OfType<DkmCustomModuleInstance>().ToArray();
            var mod = mods.FirstOrDefault(m => m.Module.Id.Mvid == Guids.CythonModuleGuid);
            if (mod == null) {
                return null;
            }

            var newLoc = new SourceLocation(
                loc.DocumentName,
                loc.TextSpan.StartLine,
                frame.BasicSymbolInfo.MethodName,
                nativeAddr
            );
            var instrAddr = DkmCustomInstructionAddress.Create(
                pythonRuntime,
                mod,
                null,
                nativeAddr.CPUInstructionPart.InstructionPointer,
                newLoc.Encode(),
                nativeAddr.CPUInstructionPart
            );

            var pyxFrame = DkmStackWalkFrame.Create(
                frame.Thread,
                instrAddr,
                frame.FrameBase,
                frame.FrameSize,
                DkmStackWalkFrameFlags.None,
                frame.Description,
                frame.Registers,
                frame.Annotations,
                null,
                null,
                DkmStackWalkFrameData.Create(stackContext.InspectionSession, pyx)
            );

            return pyxFrame;
        }
    }
}
