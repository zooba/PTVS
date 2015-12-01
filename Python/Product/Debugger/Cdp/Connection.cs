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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.PythonTools.Cdp {
    public class Connection : IDisposable {
        private readonly Dictionary<int, RequestInfo> _requestCache;
        private readonly Dictionary<int, Response> _responseCache;
        private readonly Stream _writer, _reader;
        private int _seq;

        public Connection(Stream writer, Stream reader) {
            _requestCache = new Dictionary<int, RequestInfo>();
            _responseCache = new Dictionary<int, Response>();
            _writer = writer;
            _reader = reader;
            BeginRead();
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing) {
            if (disposing) {
                _writer.Dispose();
                _reader.Dispose();
                lock (_requestCache) {
                    foreach (var r in _requestCache.Values) {
                        r.Task?.TrySetCanceled();
                    }
                }
            }
        }

        private async void BeginRead() {
            try {
                await ReadResponsesAsync();
            } catch (ObjectDisposedException) {
            }
        }

        private async Task ReadResponsesAsync() {
            var reader = new StreamReader(_reader, Encoding.UTF8);
            string line;
            while ((line = await reader.ReadLineAsync()) != null) {
                Response.Data response;
                Event.Data evt;
                if ((response = JsonConvert.DeserializeObject<Response.Data>(line)) != null &&
                    response.type == "response") {
                    RequestInfo r;
                    lock (_requestCache) {
                        _requestCache.TryGetValue(response.requestSeq, out r);
                    }

                    var resp = new Response(this, response);
                    lock (_responseCache) {
                        _responseCache[response.seq] = resp;
                    }
                    r.Task?.TrySetResult(resp);
                } else if ((evt = JsonConvert.DeserializeObject<Event.Data>(line)) != null &&
                    evt.type == "event") {
                    EventReceived?.Invoke(this, new EventReceivedEventArgs(new Event(this, evt)));
                } else {
                    Trace.TraceError("Unable to parse: " + line);
                }
            }
        }

        public async Task<Response> SendRequestAsync(Request request, CancellationToken cancellationToken) {
            int seq = request._data.seq = Interlocked.Increment(ref _seq);

            var task = new TaskCompletionSource<Response>();
            if (cancellationToken.CanBeCanceled) {
                cancellationToken.Register(() => task.TrySetCanceled());
            }

            RequestInfo r = new RequestInfo { Request = request, Task = task };
            lock (_requestCache) {
                _requestCache[seq] = r;
            }

            var str = JsonConvert.SerializeObject(r.Request._data) + "\n";
            var bytes = Encoding.UTF8.GetBytes(str);
            await _writer.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
            return await r.Task.Task;
        }

        public event EventHandler<EventReceivedEventArgs> EventReceived;

        internal Request GetRequest(int seq) {
            RequestInfo r;
            lock (_requestCache) {
                return _requestCache.TryGetValue(seq, out r) ? r.Request : null;
            }
        }

        internal void ClearRequest(int seq) {
            lock (_requestCache) {
                RequestInfo r;
                if (_requestCache.TryGetValue(seq, out r)) {
                    _requestCache.Remove(seq);
                    r.Task.Task.Dispose();
                }
            }
        }

        internal void ClearResponse(int seq) {
            lock (_responseCache) {
                _responseCache.Remove(seq);
            }
        }

        private struct RequestInfo {
            public Request Request { get; set; }
            public TaskCompletionSource<Response> Task { get; set; }
        }
    }
}
