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
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Extensions.Build;

namespace Microsoft.PythonTools.Workspace {
    class RequirementsTxtContext {
        public const string ContextType = "{9545F0EB-133D-4148-9B6F-8312BCA5DBC2}";
        public static readonly Guid ContextTypeGuid = new Guid(ContextType);

        public string InterpreterName { get; set; }
        public string InterpreterPath { get; set; }

        public static IEnumerable<RequirementsTxtContext> GetContextsFromInterpreters(IEnumerable<string> interpreters) {
            foreach (var interpreter in interpreters) {
                // TODO: Better selection of unambiguous names
                var intName = PathUtils.GetParent(interpreter);
                if (PathUtils.GetFileOrDirectoryName(intName).Equals("scripts", StringComparison.InvariantCultureIgnoreCase)) {
                    intName = PathUtils.GetParent(intName);
                }
                intName = PathUtils.GetFileOrDirectoryName(intName);
                if (string.IsNullOrEmpty(intName)) {
                    continue;
                }

                yield return new RequirementsTxtContext {
                    InterpreterName = intName,
                    InterpreterPath = interpreter
                };
            }
        }
    }

    [ExportFileContextProvider(ProviderType, RequirementsTxtContext.ContextType)]
    class RequirementsTxtContextProvider : IWorkspaceProviderFactory<IFileContextProvider> {
        public const string ProviderType = "{6B5CDE8E-F1C4-4FD2-9A02-1B7C94DA7548}";
        public static readonly Guid ProviderTypeGuid = new Guid(ProviderType);

        public IFileContextProvider CreateProvider(IWorkspace workspaceContext) {
            return new FileContextProvider(workspaceContext);
        }

        private class FileContextProvider : IFileContextProvider {
            private readonly IWorkspace _workspace;

            public FileContextProvider(IWorkspace workspace) {
                _workspace = workspace;
            }

            public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(
                string filePath,
                CancellationToken cancellationToken
            ) {
                var res = new List<FileContext>();

                var interpreters = new StringCollector();
                await _workspace.GetFindFilesService().FindFilesAsync("python.exe", interpreters, cancellationToken);

                foreach (var ctxt in RequirementsTxtContext.GetContextsFromInterpreters(interpreters.Collection)) {
                    res.Add(new FileContext(
                        ProviderTypeGuid,
                        RequirementsTxtContext.ContextTypeGuid,
                        ctxt,
                        new[] { filePath }
                    ));
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

    [ExportFileContextActionProvider(ProviderType, RequirementsTxtContext.ContextType)]
    class RequirementsTxtActionProvider : IWorkspaceProviderFactory<IFileContextActionProvider> {
        private const string ProviderType = "{A9F1584A-936F-4128-982C-2C1D1C15C856}";

        public IFileContextActionProvider CreateProvider(IWorkspace workspaceContext) {
            return new FileContextActionProvider();
        }

        class FileContextActionProvider : IFileContextActionProvider {
            public async Task<IReadOnlyList<IFileContextAction>> GetActionsAsync(
                string filePath,
                FileContext fileContext,
                CancellationToken cancellationToken
            ) {
                var res = new List<IFileContextAction>();
                res.Add(new InstallRequirementsTxtAction(filePath, fileContext));
                return res;
            }
        }
    }

    class InstallRequirementsTxtAction : IFileContextAction {
        private readonly string _path;

        public InstallRequirementsTxtAction(string filePath, FileContext fileContext) {
            _path = filePath;
            Source = fileContext;
            if (!Source.ContextType.Equals(RequirementsTxtContext.ContextTypeGuid)) {
                throw new ArgumentException("fileContext");
            }
        }

        private RequirementsTxtContext Context => (RequirementsTxtContext)Source.Context;

        public string DisplayName => "Install into {0}".FormatUI(Context.InterpreterName);
        public FileContext Source { get; }

        public async Task<IFileContextActionResult> ExecuteAsync(
            IProgress<IFileContextActionProgressUpdate> progress,
            CancellationToken cancellationToken
        ) {
            using (var p = ProcessOutput.RunHiddenAndCapture(
                Context.InterpreterPath, "-m", "pip", "install", "-r", _path
            )) {
                var exitCode = await p;

                if (exitCode != 0) {
                    return new FileContextActionResult(false);
                }
            }

            return new FileContextActionResult(true);
        }
    }
    
}
