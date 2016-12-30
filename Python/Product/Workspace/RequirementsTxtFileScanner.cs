using System;
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

namespace Microsoft.PythonTools.Workspace {
    [ExportFileScanner(ProviderType, "RequirementsTxt", "*.txt", typeof(IReadOnlyCollection<FileDataValue>))]
    sealed class RequirementsTxtFileScannerProvider : IWorkspaceProviderFactory<IFileScanner> {
        public const string ProviderType = "{19A8985F-36FC-4D77-A502-44B8C59B2D44}";
        public static readonly Guid ProviderTypeGuid = new Guid(ProviderType);

        public IFileScanner CreateProvider(IWorkspace workspaceContext) {
            return new RequirementsTxtFileScanner(workspaceContext);
        }
    }

    class RequirementsTxtFileScanner : IFileScanner {
        private readonly IWorkspace _workspace;

        public RequirementsTxtFileScanner(IWorkspace workspace) {
            _workspace = workspace;
        }

        private async Task<IReadOnlyCollection<FileDataValue>> ScanFileDataValue(string filePath, CancellationToken cancellationToken) {
            if (!"requirements.txt".Equals(PathUtils.GetFileOrDirectoryName(filePath), StringComparison.OrdinalIgnoreCase)) {
                return Array.Empty<FileDataValue>();
            }

            var fdv = new List<FileDataValue>();
            //foreach (var config in _workspace.GetComponentModel().GetService<IInterpreterRegistryService>().Configurations) {
            //    fdv.Add(new FileDataValue(
            //        BuildConfigurationContext.ContextTypeGuid,
            //        "Install into " + config.Description,
            //        null,
            //        null,
            //        config.Id
            //    ));
            //}
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
