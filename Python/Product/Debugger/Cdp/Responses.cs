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

namespace Microsoft.PythonTools.Cdp {
    public class Response : IDisposable {
        protected readonly Connection _connection;
        internal readonly Data _data;
        private bool _isDisposed;

        public Response(Response from) {
            _connection = from._connection;
            _data = from._data;
        }

        internal Response(Connection connection, Data data) {
            _connection = connection;
            _data = data;
        }

        protected void ThrowIfDisposed() {
            if (_isDisposed) {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing) {
            if (_isDisposed) {
                return;
            }
            _isDisposed = true;
            if (disposing) {
                _connection.ClearRequest(_data.requestSeq);
                _connection.ClearResponse(_data.seq);
            }
        }

        public string Command => _data.command;
        public bool Success => _data.success;
        public string Message => _data.message;
        public IReadOnlyDictionary<string, object> RawBody => _data.body;

        public Request Request {
            get {
                ThrowIfDisposed();
                return _connection.GetRequest(_data.requestSeq);
            }
        }

        protected internal class Data {
            public string type = "response";
            public int seq = -1;
            public int requestSeq = -1;
            public bool success = false;
            public string command = string.Empty;
            public string message = string.Empty;
            public Dictionary<string, object> body = null;
        }
    }

    public sealed class EvaluateResponse : Response {
        private EvaluateResponse(Response from) : base(from) { }

        public string Result => RawBody["result"] as string;

        public static EvaluateResponse TryCreate(Response from) {
            if (!from.Success || from.Command != "evaluate" || from.RawBody == null) {
                return null;
            }
            if (!from.RawBody.ContainsKey("result")) {
                return null;
            }
            return new EvaluateResponse(from);
        }
    }
}
