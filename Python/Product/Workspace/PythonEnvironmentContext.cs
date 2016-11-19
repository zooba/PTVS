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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Workspace;

namespace Microsoft.PythonTools.Workspace {
    class PythonEnvironmentContext {
        public const string ContextType = "{9545F0EB-133D-4148-9B6F-8312BCA5DBC2}";
        public static readonly Guid ContextTypeGuid = new Guid(ContextType);

        public PythonEnvironmentContext(InterpreterConfiguration config) {
            Configuration = config;
        }

        public InterpreterConfiguration Configuration { get; }

        public static async Task<IReadOnlyList<PythonEnvironmentContext>> GetVirtualEnvironmentsAsync(
            IWorkspace workspace,
            CancellationToken cancellationToken
        ) {
            var res = new List<PythonEnvironmentContext>();

            var interpreters = new StringCollector();
            await workspace.GetFindFilesService().FindFilesAsync("python.exe", interpreters, cancellationToken);

            foreach (var interpreter in interpreters.Collection) {
                var prefix = PathUtils.GetParent(interpreter);
                if (PathUtils.GetFileOrDirectoryName(prefix).Equals("scripts", StringComparison.InvariantCultureIgnoreCase)) {
                    prefix = PathUtils.GetParent(prefix);
                }
                var intName = PathUtils.GetFileOrDirectoryName(prefix);
                if (string.IsNullOrEmpty(intName)) {
                    continue;
                }

                res.Add(new PythonEnvironmentContext(new InterpreterConfiguration(
                    intName,
                    intName,
                    prefix,
                    interpreter,
                    interpreter,
                    "PYTHONPATH"
                )));
            }

            return res;
        }

        class StringCollector : IProgress<string> {
            public readonly List<string> Collection = new List<string>();

            public void Report(string value) {
                Collection.Add(value);
            }
        }
    }
}
