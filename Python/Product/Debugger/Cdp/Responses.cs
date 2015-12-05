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

        protected int TryGetFromBody(string key, int defaultValue) {
            object obj;
            return (_data.body.TryGetValue(key, out obj) ? obj as int? : null) ?? defaultValue;
        }

        protected IReadOnlyList<T> TryGetListFromBody<T>(string key) {
            object obj;
            JArray list;
            if (!_data.body.TryGetValue(key, out obj) || (list = obj as JArray) == null) {
                return null;
            }
            return list.Values<T>().ToArray();
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

        public int VariablesReference => TryGetFromBody("variablesReference", 0);

        private static KeyValuePair<string, string> AsPair(JToken token) {
            return new KeyValuePair<string, string>(token.Value<string>("contentType"), token.Value<string>("value"));
        }

        public string GetFinalDisplay(string contentType) {
            return TryGetFromBody<JArray>("display")
                ?.LastOrDefault()
                ?.Select(AsPair)
                .FirstOrDefault(kv => kv.Key == contentType)
                .Value;
        }

        public IReadOnlyList<IReadOnlyCollection<KeyValuePair<string, string>>> Display {
            get {
                return TryGetFromBody<JArray>("display")
                    ?.Select(a => a.Select(AsPair).ToArray())
                    .ToArray();
            }
        }

        public static EvaluateResponse TryCreate(Response from) {
            if (!from.Success ||
                // We can get an EvaluateResponse when launching code
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

    public struct ScopeInfo {
        public string Name;
        public int VariablesReference;
        public bool Expensive;
    }

    public sealed class ScopesResponse : Response {
        private ScopesResponse(Response from) : base(from) { }

        public IReadOnlyCollection<ScopeInfo> Scopes {
            get {
                var list = TryGetFromBody<JArray>("scopes");
                if (list == null) {
                    return null;
                }
                return list.Select(j => new ScopeInfo {
                    Name = j.Value<string>("name"),
                    VariablesReference = j.Value<int>("variablesReference"),
                    Expensive = j.Value<bool>("expensive")
                }).ToArray();
            }
        }

        public static ScopesResponse TryCreate(Response from) {
            if (!from.Success || from.Command != "scopes" || from.RawBody == null) {
                return null;
            }
            if (!from.RawBody.ContainsKey("scopes")) {
                return null;
            }

            return new ScopesResponse(from);
        }
    }

    public struct VariableInfo {
        public string Name;
        public string Value;
        public string Type;
        public int VariablesReference;
    }

    public sealed class VariablesResponse : Response {
        private VariablesResponse(Response from) : base(from) { }

        public IReadOnlyCollection<VariableInfo> Variables {
            get {
                var list = TryGetFromBody<JArray>("variables");
                if (list == null) {
                    return null;
                }
                return list.Select(j => new VariableInfo {
                    Name = j.Value<string>("name"),
                    Value = j.Value<string>("value"),
                    Type = j.Value<string>("type"),
                    VariablesReference = j.Value<int>("variablesReference")
                }).ToArray();
            }
        }

        public static VariablesResponse TryCreate(Response from) {
            if (!from.Success || from.Command != "variables" || from.RawBody == null) {
                return null;
            }
            if (!from.RawBody.ContainsKey("variables")) {
                return null;
            }

            return new VariablesResponse(from);
        }
    }



    public sealed class SourceResponse : Response {
        private SourceResponse(Response from) : base(from) { }

        public string Content => RawBody["content"] as string;

        public static SourceResponse TryCreate(Response from) {
            if (!from.Success || from.Command != "source" || from.RawBody == null) {
                return null;
            }
            if (!from.RawBody.ContainsKey("content")) {
                return null;
            }

            return new SourceResponse(from);
        }
    }
}
