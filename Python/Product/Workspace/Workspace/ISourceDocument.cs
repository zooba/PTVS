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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Intellisense;

namespace Microsoft.PythonTools.Workspace {
    public interface ISourceDocumentSnapshot : IDisposable {
        /// <summary>
        /// A text reader for the contents of the document. If this is not-null,
        /// it should be used in preference to <see cref="Stream"/>.
        /// </summary>
        TextReader Reader { get; }
        
        /// <summary>
        /// Contains a raw stream for the contents of the document. If
        /// <see cref="Reader"/> is null, the caller is responsible for reading
        /// the contents and determining the encoding from this stream.
        /// </summary>
        Stream Stream { get; }

        /// <summary>
        /// An IntelliSense cookie identifying this snapshot.
        /// </summary>
        IIntellisenseCookie Cookie { get; }

        /// <summary>
        /// The original document.
        /// </summary>
        ISourceDocument Document { get; }

        /// <summary>
        /// The version of the document returned in the snapshot.
        /// </summary>
        int Version { get; }
    }

    public interface ISourceDocument {
        Task<ISourceDocumentSnapshot> ReadAsync(CancellationToken cancellationToken);

        string Moniker { get; }
        int Version { get; }
    }

    public sealed class SourcelessDocument : ISourceDocument {
        private readonly string _moniker;

        public SourcelessDocument(string moniker) {
            _moniker = moniker;
        }

        public string Moniker => _moniker;
        public int Version => 0;

        public Task<ISourceDocumentSnapshot> ReadAsync(CancellationToken cancellationToken) {
            return Task.FromResult<ISourceDocumentSnapshot>(new Snapshot(this));
        }

        sealed class Snapshot : ISourceDocumentSnapshot {
            public Snapshot(ISourceDocument document) {
                Document = document;
            }

            public override bool Equals(object obj) => Document.Equals((obj as Snapshot)?.Document);
            public override int GetHashCode() => GetType().GetHashCode() ^ Document.GetHashCode();

            public void Dispose() { }

            public Stream Stream => Stream.Null;
            public TextReader Reader => null;
            public IIntellisenseCookie Cookie => null;
            public ISourceDocument Document { get; }
            public int Version => 0;
        }
    }

    public sealed class StringLiteralDocument : ISourceDocument {
        private string _document;
        private int _version;

        public StringLiteralDocument(string document, string moniker = "<string>") {
            _document = document;
            Moniker = moniker;
        }

        public string Document {
            get { return _document; }
            set {
                lock (_document) {
                    _document = value;
                    _version += 1;
                }
            }
        }

        public string Moniker { get; }

        public async Task<ISourceDocumentSnapshot> ReadAsync(CancellationToken cancellationToken) {
            lock (_document) {
                return new Snapshot(this, Document, Version);
            }
        }

        public int Version => _version;

        sealed class Snapshot : ISourceDocumentSnapshot {
            private readonly Lazy<Stream> _stream;

            public Snapshot(ISourceDocument document, string content, int version) {
                Reader = new StringReader(content);
                Document = document;
                Version = version;

                _stream = new Lazy<Stream>(() => {
                    var ms = new MemoryStream();
                    using (var sw = new StreamWriter(ms, new UTF8Encoding(true), 4096, true)) {
                        sw.Write(content);
                    }
                    ms.Seek(0, SeekOrigin.Begin);
                    return ms;
                });
            }

            public override bool Equals(object obj) {
                return obj is Snapshot && ((Snapshot)obj).Version == Version && Document.Equals(((Snapshot)obj).Document);
            }

            public override int GetHashCode() {
                return GetType().GetHashCode() ^ Version.GetHashCode() ^ Document.GetHashCode();
            }

            public void Dispose() {
                if (_stream.IsValueCreated) {
                    _stream.Value.Dispose();
                }
            }

            public Stream Stream => _stream.Value;
            public TextReader Reader { get; }
            public IIntellisenseCookie Cookie => null;
            public ISourceDocument Document { get; }
            public int Version { get; }
        }

    }
}
