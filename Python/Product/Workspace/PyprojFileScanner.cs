using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Project;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace Microsoft.PythonTools.Workspace {
    [ExportFileScanner(
        FileScannerOptions.None,
        ProviderType,
        "PyProj",
        new[] { ".pyproj" },
        new[] { typeof(IReadOnlyDictionary<string, object>) },
        ProviderPriority.AboveNormal
    )]
    class PyprojFileScannerProvider : IWorkspaceProviderFactory<IFileScanner> {
        const string ProviderType = "4B13B458-3E7A-4BFE-B314-6753688F4298";
        public static readonly Guid ProviderGuid = new Guid(ProviderType);


        [Import(typeof(SVsServiceProvider))]
        public IServiceProvider _site = null;

        public IFileScanner CreateProvider(IWorkspace workspaceContext) {
            return new PyprojFileScanner(workspaceContext, _site);
        }
    }

    class PyprojFileScanner : IFileScanner {
        private readonly IWorkspace _workspace;
        private readonly IServiceProvider _site;

        public PyprojFileScanner(IWorkspace workspaceContext, IServiceProvider site) {
            _workspace = workspaceContext;
            _site = site;
        }

        public async Task<T> ScanContentAsync<T>(
            string filePath,
            CancellationToken cancellationToken
        ) where T : class {
            if (!typeof(T).IsEquivalentTo(typeof(IReadOnlyDictionary<string, object>))) {
                throw new NotImplementedException();
            }

            var configs = await _workspace.JTF.RunAsync(VsTaskRunContext.UIThreadNormalPriority, async () => {
                var svc = _site.GetComponentModel().GetService<IInterpreterRegistryService>();
                return svc.Configurations.ToArray();
            });

            var content = new Dictionary<string, object>();

            LaunchConfiguration config = null;
            IReadOnlyDictionary<string, IReadOnlyList<string>> files = null;

            try {
                await _workspace.JTF.RunAsync(VsTaskRunContext.UIThreadNormalPriority, async () => {
                    await _workspace.JTF.SwitchToMainThreadAsync(cancellationToken);
                    PythonProjectPackage.ReadProjectFile(_site, filePath, out config, out files);
                });
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Debug.Fail(ex.ToUnhandledExceptionMessage(GetType()));
                return null;
            }

            if (config != null) {
                content["LaunchConfiguration"] = config;
            }
            if (files != null) {
                content["Items"] = files;
            }

            return content as T;
        }
    }
}
