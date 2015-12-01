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
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        protected T TryGetFromBody<T>(string key) where T : class {
            object obj;
            return _data.body.TryGetValue(key, out obj) ? obj as T : null;
        }

        protected IReadOnlyList<T> TryGetListFromBody<T>(string key) {
            object obj;
            JArray list;
            if (!_data.body.TryGetValue(key, out obj) || (list = obj as JArray) == null) {
                return null;
            }
            try {
                return list.Values<T>().ToArray();
            } catch (InvalidCastException) {
                return null;
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

        public IReadOnlyList<string> Reprs => TryGetListFromBody<string>("reprs");

        public IReadOnlyList<IReadOnlyList<string>> Members {
            get {
                object obj;
                JArray listOfList;
                if (!RawBody.TryGetValue("members", out obj) || (listOfList = obj as JArray) == null) {
                    return null;
                }
                try {
                    return listOfList.Select(a => a.Values<string>().ToArray()).ToArray();
                } catch (InvalidCastException) {
                    return null;
                }
            }
        }

        public IReadOnlyList<object> CallSignatures => TryGetFromBody<IReadOnlyList<object>>("callSignatures");

        public IReadOnlyList<string> Docs => TryGetListFromBody<string>("docs");

        public static EvaluateResponse TryCreate(Response from) {
            if (!from.Success ||
                // If we launch with code, we can get an EvaluateResponse
                (from.Command != "evaluate" && from.Command != "launch") ||
                from.RawBody == null) {
                return null;
            }
            if (!from.RawBody.ContainsKey("result")) {
                return null;
            }
            return new EvaluateResponse(from);
        }
    }
}
