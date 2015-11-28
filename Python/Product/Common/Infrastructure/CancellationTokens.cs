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
using System.Threading;

namespace Microsoft.PythonTools.Infrastructure {
    public static class CancellationTokens {
        public static CancellationToken After5s {
            get {
                return new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;
            }
        }

        public static CancellationToken After1s {
            get {
                return new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token;
            }
        }

        public static CancellationToken After500ms {
            get {
                return new CancellationTokenSource(TimeSpan.FromMilliseconds(500)).Token;
            }
        }

        public static CancellationToken After100ms {
            get {
                return new CancellationTokenSource(TimeSpan.FromMilliseconds(100)).Token;
            }
        }
    }
}
