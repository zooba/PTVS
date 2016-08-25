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
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Workspace;
using Microsoft.VisualStudio.Text;

namespace Microsoft.PythonTools.Intellisense {
    sealed class TextBufferSourceDocument : ISourceDocument {
        private readonly ITextBuffer _buffer;

        public TextBufferSourceDocument(string moniker, ITextBuffer buffer) {
            Moniker = moniker;
            _buffer = buffer;
        }

        public ITextBuffer Buffer => _buffer;

        public string Moniker { get; }

        public int Version => _buffer.CurrentSnapshot.Version.VersionNumber;

        public override bool Equals(object obj) {
            // Must be the same type, and if so must have the same buffer.
            return _buffer == (obj as TextBufferSourceDocument)?._buffer;
        }

        public override int GetHashCode() {
            return GetType().GetHashCode() ^ _buffer.GetHashCode();
        }

        public async Task<ISourceDocumentSnapshot> ReadAsync(CancellationToken cancellationToken) {
            return new Snapshot(this, _buffer.CurrentSnapshot);
        }

        internal sealed class Snapshot : ISourceDocumentSnapshot {
            private readonly ITextSnapshot _snapshot;
            private readonly Lazy<Stream> _stream;

            public Snapshot(ISourceDocument document, ITextSnapshot snapshot) {
                Document = document;
                _snapshot = snapshot;
                Cookie = new SnapshotCookie(snapshot);
                Reader = new TextSnapshotToTextReader(_snapshot);

                _stream = new Lazy<Stream>(() => {
                    var ms = new MemoryStream();
                    using (var sw = new StreamWriter(ms, new UTF8Encoding(true), 4096, true)) {
                        foreach (var line in _snapshot.Lines) {
                            sw.Write(line.GetTextIncludingLineBreak());
                        }
                    }
                    ms.Seek(0, SeekOrigin.Begin);
                    return ms;
                });
            }

            public void Dispose() {
                if (_stream.IsValueCreated) {
                    _stream.Value.Dispose();
                }
            }

            public override bool Equals(object obj) {
                var other = obj as Snapshot;
                return other != null && _snapshot == other._snapshot;
            }

            public override int GetHashCode() {
                return GetType().GetHashCode() ^ _snapshot.GetHashCode();
            }

            internal ITextSnapshot TextSnapshot => _snapshot;

            public TextReader Reader { get; }
            public Stream Stream => _stream.Value;
            public IIntellisenseCookie Cookie { get; }
            public ISourceDocument Document { get; }
            public int Version => _snapshot.Version.VersionNumber;
        }
    }

    static class TextBufferSourceDocumentExtensions {
        public static ITextBuffer GetTextBuffer(this ISourceDocument doc) {
            return (doc as TextBufferSourceDocument)?.Buffer;
        }

        public static ITextSnapshot GetTextSnapshot(this ISourceDocumentSnapshot snapshot) {
            return (snapshot as TextBufferSourceDocument.Snapshot)?.TextSnapshot;
        }
    }
}
