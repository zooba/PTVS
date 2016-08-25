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

namespace Microsoft.PythonTools.Workspace {
    public interface ISourceDocument {
        /// <summary>
        /// Returns a stream containing the original document. The caller is
        /// responsible for determining the encoding.
        /// </summary>
        Task<Stream> ReadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Returns a text reader over the document if possible, otherwise null.
        /// Callers should try this method before using <see cref="ReadAsync"/>
        /// if the intent is to convert the file contents to text.
        /// </summary>
        Task<TextReader> TryReadTextAsync(CancellationToken cancellationToken);

        string Moniker { get; }
        int Version { get; }
    }

    public sealed class SourcelessDocument : ISourceDocument {
        private readonly string _moniker;

        public SourcelessDocument(string moniker) {
            _moniker = moniker;
        }

        public string Moniker => _moniker;

        public Task<Stream> ReadAsync(CancellationToken cancellationToken) {
            return Task.FromResult(Stream.Null);
        }

        public Task<TextReader> TryReadTextAsync(CancellationToken cancellationToken) {
            return Task.FromResult<TextReader>(null);
        }

        public int Version => -1;
    }

    public sealed class StringLiteralDocument : ISourceDocument {
        public StringLiteralDocument(string document, string moniker = "<string>") {
            Document = document;
            Moniker = moniker;
        }

        public string Document { get; set; }

        public string Moniker { get; }

        public async Task<Stream> ReadAsync(CancellationToken cancellationToken) {
            var ms = new MemoryStream();
            using (var sw = new StreamWriter(ms, new UTF8Encoding(true), 4096, true)) {
                await sw.WriteAsync(Document);
            }
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        public Task<TextReader> TryReadTextAsync(CancellationToken cancellationToken) {
            return Task.FromResult<TextReader>(new StringReader(Document));
        }

        public int Version => 0;
    }
}
