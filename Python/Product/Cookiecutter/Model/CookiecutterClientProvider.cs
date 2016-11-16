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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CookiecutterTools.Infrastructure;
using Microsoft.CookiecutterTools.Interpreters;

namespace Microsoft.CookiecutterTools.Model {
    static class CookiecutterClientProvider {
        public static ICookiecutterClient Create(IServiceProvider provider, Redirector redirector) {
            var interpreter = FindCompatibleInterpreter();
            if (interpreter != null) {
                return new CookiecutterClient(provider, interpreter, redirector);
            }

            return null;
        }

        public static bool IsCompatiblePythonAvailable() {
            return FindCompatibleInterpreter() != null;
        }

        private static CookiecutterPythonInterpreter FindCompatibleInterpreter() {
            var interpreters = PythonRegistrySearch.PerformDefaultSearch();
            var compatible = interpreters
                .Where(x => File.Exists(x.Configuration.InterpreterPath))
                .OrderByDescending(x => x.Configuration.Version)
                .FirstOrDefault(x => x.Configuration.Version >= new Version(3, 3));
            return compatible != null ? new CookiecutterPythonInterpreter(compatible.Configuration.InterpreterPath) : null;
        }
    }
}
