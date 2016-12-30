﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Indexing;
using Microsoft.PythonTools.Project;
using System.IO;
using System.ComponentModel.Composition;

namespace Microsoft.PythonTools.Workspace {
    [ExportFileScanner(ProviderType, "VEnv", "*.cfg", typeof(IReadOnlyCollection<FileDataValue>))]
    sealed class VEnvFileScannerProvider : IWorkspaceProviderFactory<IFileScanner> {
        public const string ProviderType = "{A678D190-DC39-4F64-B101-50AC3C6670F8}";
        public static readonly Guid ProviderTypeGuid = new Guid(ProviderType);

        private readonly WorkspaceInterpreterFactoryProvider _factoryProvider;

        [ImportingConstructor]
        public VEnvFileScannerProvider([Import] WorkspaceInterpreterFactoryProvider factoryProvider) {
            _factoryProvider = factoryProvider;
        }

        public IFileScanner CreateProvider(IWorkspace workspaceContext) {
            return new VirtualEnvFileScanner(workspaceContext, _factoryProvider);
        }

        class VirtualEnvFileScanner : IFileScanner {
            private readonly IWorkspace _workspace;
            private readonly WorkspaceInterpreterFactoryProvider _factoryProvider;

            public VirtualEnvFileScanner(IWorkspace workspace, WorkspaceInterpreterFactoryProvider factoryProvider) {
                _workspace = workspace;
                _factoryProvider = factoryProvider;
            }


            private async Task<IReadOnlyCollection<FileDataValue>> ScanFileDataValue(string filePath, CancellationToken cancellationToken) {
                if (!"pyvenv.cfg".Equals(PathUtils.GetFileOrDirectoryName(filePath), StringComparison.OrdinalIgnoreCase)) {
                    return Array.Empty<FileDataValue>();
                }

                _factoryProvider.SetWorkspace(_workspace);
                var config = _factoryProvider.GetConfigurationFromPrefixPath(_workspace, PathUtils.GetParent(filePath));

                if (config == null) {
                    return Array.Empty<FileDataValue>();
                }

                var fdv = new List<FileDataValue>();
                fdv.Add(config);
                return fdv;
            }

            public async Task<T> ScanContentAsync<T>(string filePath, CancellationToken cancellationToken) where T : class {
                if (typeof(T).IsEquivalentTo(FileScannerTypeConstants.FileDataValuesType)) {
                    return (T)(await ScanFileDataValue(filePath, cancellationToken));
                }
                throw new NotImplementedException();
            }
        }
    }
}
