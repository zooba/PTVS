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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Extensions.Build;
using Microsoft.VisualStudio.Workspace.Extensions.VS;

namespace Microsoft.PythonTools.Workspace {
    [ExportFileContextProvider(ProviderType,
        PythonEnvironmentContext.ContextType,
        NewPythonEnvironmentContext.ContextType
    )]
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

                foreach (var ctxt in await PythonEnvironmentContext.GetEnvironmentsAsync(_workspace, cancellationToken)) {
                    res.Add(new FileContext(
                        ProviderTypeGuid,
                        PythonEnvironmentContext.ContextTypeGuid,
                        ctxt,
                        new[] { filePath }
                    ));
                }

                var newName = PathUtils.GetAbsoluteDirectoryPath(_workspace.Location, "env");
                for (int i = 1; Directory.Exists(newName) && i < int.MaxValue; ++i) {
                    newName = PathUtils.GetAbsoluteDirectoryPath(_workspace.Location, "env{0}".FormatInvariant(i));
                }

                res.Add(new FileContext(
                    ProviderTypeGuid,
                    NewPythonEnvironmentContext.ContextTypeGuid,
                    new NewPythonEnvironmentContext(PathUtils.GetFileOrDirectoryName(newName), newName),
                    new[] { filePath }
                ));

                return res;
            }

        }
    }

    [ExportFileContextActionProvider(ProviderType,
        PythonEnvironmentContext.ContextType,
        NewPythonEnvironmentContext.ContextType
    )]
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
                //res.Add(new RegenerateRequirementsTxtAction(filePath, fileContext));
                return res;
            }
        }
    }

    class InstallRequirementsTxtAction : IFileContextAction, IVsCommandItem {
        private readonly string _path;

        public InstallRequirementsTxtAction(string filePath, FileContext fileContext) {
            _path = filePath;
            Source = fileContext;
            if (!Source.ContextType.Equals(PythonEnvironmentContext.ContextTypeGuid) &&
                !Source.ContextType.Equals(NewPythonEnvironmentContext.ContextTypeGuid)) {
                throw new ArgumentException("fileContext");
            }
        }

        private PythonEnvironmentContext Context => Source.Context as PythonEnvironmentContext;
        private NewPythonEnvironmentContext NewContext => Source.Context as NewPythonEnvironmentContext;

        public string DisplayName {
            get {
                if (Context != null) {
                    return "Install into {0}".FormatUI(Context.Configuration.Description);
                }
                return "Create and install into {0}".FormatUI(NewContext.Description);
            }
        }

        public FileContext Source { get; }

        public Guid CommandGroup => WorkspaceGuids.GuidWorkspaceExplorerFileContextActionsCmdSet;
        public uint CommandId => 0x0100;

        public async Task<IFileContextActionResult> ExecuteAsync(
            IProgress<IFileContextActionProgressUpdate> progress,
            CancellationToken cancellationToken
        ) {
            InterpreterConfiguration config = Context?.Configuration;

            if (config == null && NewContext != null) {

                using (var p = ProcessOutput.RunHiddenAndCapture(
                    // TODO: Fix this
                    "py", "-3", "-m", "venv", NewContext.PrefixPath
                )) {
                    if ((await p) != 0) {
                        return new FileContextActionResult(false);
                    }
                }
                config = new InterpreterConfiguration(
                    NewContext.Description,
                    NewContext.Description,
                    NewContext.PrefixPath,
                    PathUtils.GetAbsoluteFilePath(NewContext.PrefixPath, "scripts\\python.exe")
                );
            }

            using (var p = ProcessOutput.RunHiddenAndCapture(
                config.InterpreterPath, "-m", "pip", "install", "-r", _path
            )) {
                var exitCode = await p;

                if (exitCode != 0) {
                    return new FileContextActionResult(false);
                }
            }

            return new FileContextActionResult(true);
        }
    }
    
    //class RegenerateRequirementsTxtAction : IFileContextAction {
    //    private readonly string _path;

    //    public RegenerateRequirementsTxtAction(string filePath, FileContext fileContext) {
    //        _path = filePath;
    //        Source = fileContext;
    //        if (!Source.ContextType.Equals(PythonEnvironmentContext.ContextTypeGuid)) {
    //            throw new ArgumentException("fileContext");
    //        }
    //    }

    //    private PythonEnvironmentContext Context => (PythonEnvironmentContext)Source.Context;

    //    public string DisplayName => "Regenerate from {0}".FormatUI(Context.Configuration.Description);
    //    public FileContext Source { get; }

    //    public async Task<IFileContextActionResult> ExecuteAsync(
    //        IProgress<IFileContextActionProgressUpdate> progress,
    //        CancellationToken cancellationToken
    //    ) {
    //        using (var p = ProcessOutput.RunHiddenAndCapture(
    //            Context.Configuration.InterpreterPath, "-m", "pip", "freeze"
    //        )) {
    //            var exitCode = await p;

    //            if (exitCode != 0) {
    //                return new FileContextActionResult(false);
    //            }

    //            // TODO: Merge this properly
    //            File.WriteAllLines(_path, p.StandardOutputLines);
    //        }

    //        return new FileContextActionResult(true);
    //    }
    //}
}
