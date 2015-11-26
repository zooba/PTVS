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
using Microsoft.VisualStudio.Text;

namespace Microsoft.PythonTools.Editor.Intellisense {
    sealed class TextBufferSourceDocument : ISourceDocument {
        private static readonly Encoding DefaultEncoding = new UTF8Encoding(false);
        private readonly string _moniker;
        private readonly ITextBuffer _buffer;
        private readonly ITextDocument _document;

        public TextBufferSourceDocument(ITextBuffer buffer) {
            _buffer = buffer;

            if (!_buffer.Properties.TryGetProperty(typeof(ITextDocument), out _document)) {
                _moniker = string.Format("<string:{0:N}>", Guid.NewGuid());
            } else {
                _moniker = _document.FilePath;
            }
        }

        public string Moniker => _moniker;
        public string ActualFilePath => _document?.FilePath;
        public Encoding Encoding => _document?.Encoding ?? DefaultEncoding;

        public async Task ReadAndGetCookieAsync(Action<Stream, object> action, CancellationToken cancellationToken) {
            var snapshot = _buffer.CurrentSnapshot;
            action(new TextSnapshotStream(snapshot, Encoding), snapshot);
        }

        public async Task<Stream> ReadAsync(CancellationToken cancellationToken) {
            return new TextSnapshotStream(_buffer.CurrentSnapshot, Encoding);
        }

    }
}
