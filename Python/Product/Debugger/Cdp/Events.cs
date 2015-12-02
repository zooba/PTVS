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

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.PythonTools.Cdp {
    public class Event {
        private Connection _connection;
        internal Data _data;

        protected Event(Event from) {
            _connection = from._connection;
            _data = from._data;
        }

        internal Event(Connection connection, Data data) {
            _connection = connection;
            _data = data;
        }

        public string EventName => _data.event_;
        public IReadOnlyDictionary<string, object> RawBody => _data.body;

        protected T TryGetFromBody<T>(string key) where T : class {
            object obj;
            return _data.body.TryGetValue(key, out obj) ? obj as T : null;
        }

        internal class Data {
            public string type = "event";
            public int seq;
            [JsonProperty("event")]
            public string event_;
            public Dictionary<string, object> body = new Dictionary<string, object>();
        }
    }

    public class OutputEvent : Event {
        private OutputEvent(Event from) : base(from) { }

        public string Category => TryGetFromBody<string>("category") ?? "console";
        public string Output => (string)RawBody["output"];

        public static OutputEvent TryCreate(Event from) {
            if (from.EventName != "output" || !from.RawBody.ContainsKey("output")) {
                return null;
            }
            return new OutputEvent(from);
        }
    }
}
