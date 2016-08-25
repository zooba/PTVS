using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;

namespace Microsoft.PythonTools.Workspace.Intellisense {
    class SnapshotSourceDocument : ISourceDocument {
        private readonly ITextSnapshot _snapshot;

        public SnapshotSourceDocument(string moniker, ITextSnapshot snapshot) {
            Moniker = moniker;
            _snapshot = snapshot;
        }

        public string Moniker { get; }

        public int Version => _snapshot.Version.VersionNumber;

        public Task<TextReader> TryReadTextAsync(CancellationToken cancellationToken) {
            return Task.FromResult<TextReader>(new TextSnapshotToTextReader(_snapshot));
        }

        public async Task<Stream> ReadAsync(CancellationToken cancellationToken) {
            var ms = new MemoryStream();
            using (var sw = new StreamWriter(ms, new UTF8Encoding(true), 4096, true)) {
                foreach (var line in _snapshot.Lines) {
                    await sw.WriteAsync(line.GetTextIncludingLineBreak());
                }
            }
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }
    }
}
