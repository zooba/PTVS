using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace Microsoft.PythonTools.Workspace {
    [ExportFileScanner(
        ProviderType,
        "Python",
        new[] { PythonConstants.FileExtension, PythonConstants.WindowsFileExtension },
        new[] { typeof(IReadOnlyCollection<FileDataValue>), typeof(IReadOnlyCollection<SymbolDefinition>) })]
    class PythonFileScannerProvider : IWorkspaceProviderFactory<IFileScanner> {
        public const string ProviderType = "{A0FEFC7A-4DCF-4638-A723-7C018847B3CD}";
        public static readonly Guid ProviderTypeGuid = new Guid(ProviderType);

        public IFileScanner CreateProvider(IWorkspace workspaceContext) {
            return new PythonFileScanner(workspaceContext);
        }
    }

    class PythonFileScanner : IFileScanner {
        private IWorkspace _workspace;

        public PythonFileScanner(IWorkspace workspaceContext) {
            this._workspace = workspaceContext;
        }

        private async Task<IReadOnlyCollection<FileDataValue>> ScanFileDataValueAsync(string filePath, CancellationToken cancellationToken) {
            // TODO: Return file data
            return Array.Empty<FileDataValue>();
        }

        private async Task<IReadOnlyCollection<SymbolDefinition>> ScanSymbolsAsync(string filePath, CancellationToken cancellationToken) {
            // TODO: Return symbol information
            return Array.Empty<SymbolDefinition>();
        }

        public async Task<T> ScanContentAsync<T>(string filePath, CancellationToken cancellationToken) where T : class {
            if (typeof(T).IsEquivalentTo(FileScannerTypeConstants.FileDataValuesType)) {
                return (T)(await ScanFileDataValueAsync(filePath, cancellationToken));
            }
            if (typeof(T).IsEquivalentTo(FileScannerTypeConstants.SymbolsDefinitionsType)) {
                return (T)(await ScanSymbolsAsync(filePath, cancellationToken));
            }
            throw new NotImplementedException();
        }
    }
}
