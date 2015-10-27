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

namespace Microsoft.PythonTools.DkmDebugger {
    internal static class NativeMethods {
        public const int MEM_COMMIT = 0x1000;
        public const int MEM_RESERVE = 0x2000;
        public const int MEM_RELEASE = 0x8000;

        public const int PAGE_READWRITE = 0x04;
    }

    public enum EXCEPTION_CODE : uint {
        EXCEPTION_ACCESS_VIOLATION = 0xc0000005,
        EXCEPTION_ARRAY_BOUNDS_EXCEEDED = 0xc000008c,
        EXCEPTION_BREAKPOINT = 0x80000003,
        EXCEPTION_DATATYPE_MISALIGNMENT = 0x80000002,
        EXCEPTION_FLT_DENORMAL_OPERAND = 0xc000008d,
        EXCEPTION_FLT_DIVIDE_BY_ZERO = 0xc000008e,
        EXCEPTION_FLT_INEXACT_RESULT = 0xc000008f,
        EXCEPTION_FLT_INVALID_OPERATION = 0xc0000090,
        EXCEPTION_FLT_OVERFLOW = 0xc0000091,
        EXCEPTION_FLT_STACK_CHECK = 0xc0000092,
        EXCEPTION_FLT_UNDERFLOW = 0xc0000093,
        EXCEPTION_GUARD_PAGE = 0x80000001,
        EXCEPTION_ILLEGAL_INSTRUCTION = 0xc000001d,
        EXCEPTION_IN_PAGE_ERROR = 0xc0000006,
        EXCEPTION_INT_DIVIDE_BY_ZERO = 0xc0000094,
        EXCEPTION_INT_OVERFLOW = 0xc0000095,
        EXCEPTION_INVALID_DISPOSITION = 0xc0000026,
        EXCEPTION_INVALID_HANDLE = 0xc0000008,
        EXCEPTION_NONCONTINUABLE_EXCEPTION = 0xc0000025,
        EXCEPTION_PRIV_INSTRUCTION = 0xc0000096,
        EXCEPTION_SINGLE_STEP = 0x80000004,
        EXCEPTION_STACK_OVERFLOW = 0xc00000fd,
        STATUS_UNWIND_CONSOLIDATE = 0x80000029,
    }
}
