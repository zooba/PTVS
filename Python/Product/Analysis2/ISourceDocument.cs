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
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis {
    public interface ISourceDocument {
        Task<Stream> ReadAsync();

        string Moniker { get; }
    }

    public sealed class StringLiteralDocument : ISourceDocument {
        private string _document;
        private readonly string _moniker;

        public StringLiteralDocument(string document, string moniker = "<string>") {
            _document = document;
            _moniker = moniker;
        }

        public string Document {
            get { return _document; }
            set { _document = value; }
        }

        public string Moniker {
            get { return _moniker; }
        }

        public async Task<Stream> ReadAsync() {
            var ms = new MemoryStream();
            using (var sw = new StreamWriter(ms, new UTF8Encoding(false), 4096, true)) {
                await sw.WriteAsync(_document);
            }
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }
    }
}
