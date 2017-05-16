using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Extensions.Build;

namespace Microsoft.PythonTools.Workspace {
    [ExportFileContextProvider(
        ProviderType,
        ProviderPriority.Normal,
        new[] { typeof(string) },
        BuildActionContext.ContextType
    )]
    public sealed class PyprojFileContextProviderCreator : IWorkspaceProviderFactory<IFileContextProvider> {
        public const string ProviderType = "5C7104C6-BCAF-4A06-981A-4313D165C14A";
        public static readonly Guid ProviderGuid = new Guid(ProviderType);

        public IFileContextProvider CreateProvider(IWorkspace workspaceContext) {
            return new PyprojFileContextProvider(workspaceContext);
        }
    }

    class PyprojFileContextProvider : IFileContextProvider {
        private readonly IWorkspace _workspace;

        private readonly ConcurrentDictionary<string, PyprojContext> _projects;

        public PyprojFileContextProvider(IWorkspace workspace) {
            _workspace = workspace;
            _projects = new ConcurrentDictionary<string, PyprojContext>();
        }

        public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(
            string filePath,
            CancellationToken cancellationToken
        ) {
            await UpdateProjectAsync(cancellationToken);

            var result = new List<FileContext>();

            foreach (var project in _projects.Values) {
                if (project.Contains(filePath)) {
                    result.Add(new FileContext(
                        PyprojFileContextProviderCreator.ProviderGuid,
                        PyprojContext.ContextGuid,
                        project,
                        project.AllFiles,
                        project.Name
                    ));
                }
                cancellationToken.ThrowIfCancellationRequested();
            }

            return result;
        }

        private async Task UpdateProjectAsync(CancellationToken cancellationToken) {
            var data = await _workspace.GetIndexWorkspaceService().GetFilesDataValuesAsync<IReadOnlyDictionary<string, object>>(
                PyprojFileScannerProvider.ProviderGuid,
                cancellationToken
            );

            foreach (var project in data) {
                foreach (var projectData in project.Value) {
                    object o;
                    LaunchConfiguration config;
                    if (!projectData.Value.TryGetValue("LaunchConfiguration", out o) ||
                        (config = o as LaunchConfiguration) == null) {
                        continue;
                    }

                    IReadOnlyDictionary<string, IReadOnlyList<string>> items;
                    if (!projectData.Value.TryGetValue("Items", out o) ||
                        (items = o as IReadOnlyDictionary<string, IReadOnlyList<string>>) == null) {
                        continue;
                    }

                    _projects[project.Key] = new PyprojContext(
                        project.Key,
                        items.SelectMany(i => i.Value),
                        config
                    );
                }
            }
        }
    }

}
