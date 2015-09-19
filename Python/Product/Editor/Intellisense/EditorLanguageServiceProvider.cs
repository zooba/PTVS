using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Projection;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudioTools;

namespace Microsoft.PythonTools.Editor.Intellisense {
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(ContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Analyzable)]
    class EditorLanguageServiceProvider : IVsTextViewCreationListener {
        [Import]
        internal IVsEditorAdaptersFactoryService _adapterService = null;
        [Import]
        internal IInterpreterConfigurationService _configService = null;
        [Import]
        internal PythonFileContextProvider _contextProvider = null;
        [Import]
        internal PythonLanguageServiceProvider _serviceProvider = null;

        public void VsTextViewCreated(IVsTextView textViewAdapter) {
            var textView = _adapterService.GetWpfTextView(textViewAdapter);
            if (textView == null) {
                return;
            }

            // TODO: Handle all exceptions
            CreateViewAsync(textView, CancellationToken.None).DoNotWait();
        }

        private async Task CreateViewAsync(ITextView textView, CancellationToken cancellationToken) {
            textView.BufferGraph.GraphBuffersChanged += BufferGraph_GraphBuffersChanged;
            await GraphChangedAsync(textView.BufferGraph, null, null, cancellationToken);
        }

        private async Task UpdateBufferPropertiesAsync(ITextBuffer buffer, CancellationToken cancellationToken) {
            var document = buffer.Properties.GetOrCreateSingletonProperty<ISourceDocument>(
                () => new TextBufferSourceDocument(buffer)
            );

            PythonFileContext context;
            if (!buffer.Properties.TryGetProperty(typeof(PythonFileContext), out context)) {
                IReadOnlyCollection<PythonFileContext> contexts;
                contexts = await _contextProvider.GetOrCreateContextsForFileAsync(
                    null,
                    document.Moniker,
                    cancellationToken
                );
                // TODO: Handle multiple contexts
                if (contexts.Any()) {
                    buffer.Properties[typeof(PythonFileContext)] = context = contexts.First();
                }
            }

            PythonLanguageService langService;
            if (!buffer.Properties.TryGetProperty(typeof(PythonLanguageService), out langService)) {
                buffer.Properties[typeof(PythonLanguageService)] = langService = await _serviceProvider.GetServiceAsync(
                    _configService.DefaultInterpreter,
                    _contextProvider,
                    cancellationToken
                );
            }

            if (context != null) {
                await langService.AddFileContextAsync(context, cancellationToken);
            }

            buffer.Changed += Buffer_Changed;
        }

        private void ClearBufferProperties(ITextBuffer buffer) {
            buffer.Properties.RemoveProperty(typeof(ISourceDocument));
            buffer.Properties.RemoveProperty(typeof(PythonFileContext));
            buffer.Changed -= Buffer_Changed;
        }

        private void BufferGraph_GraphBuffersChanged(object sender, GraphBuffersChangedEventArgs e) {
            var graph = sender as IBufferGraph;
            if (graph == null) {
                Debug.Fail("Invalid sender");
                return;
            }

            GraphChangedAsync(graph, e.AddedBuffers, e.RemovedBuffers, CancellationToken.None).DoNotWait();
        }

        private async Task GraphChangedAsync(
            IBufferGraph graph,
            IEnumerable<ITextBuffer> changed,
            IEnumerable<ITextBuffer> removed,
            CancellationToken cancellationToken
        ) {
            if (removed != null) {
                foreach (var buffer in removed) {
                    ClearBufferProperties(buffer);
                }
            }

            foreach(var buffer in changed ?? graph.GetTextBuffers(b => b.ContentType.IsOfType(ContentType.Name))) {
                await UpdateBufferPropertiesAsync(buffer, cancellationToken);
            }
        }

        private void Buffer_Changed(object sender, TextContentChangedEventArgs e) {
            var buffer = e.After.TextBuffer;
            ISourceDocument document;
            PythonFileContext context;

            if (buffer.Properties.TryGetProperty(typeof(ISourceDocument), out document) &&
                buffer.Properties.TryGetProperty(typeof(PythonFileContext), out context)) {
                context.NotifyDocumentContentChanged(document);
            }
        }
    }
}
