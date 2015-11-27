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
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Common.Infrastructure {
    public sealed class AsyncMutex : IDisposable {
        private int _count;
        private readonly List<TaskCompletionSource<IDisposable>> _waiters;

        public AsyncMutex() {
            _count = 1;
            _waiters = new List<TaskCompletionSource<IDisposable>>();
        }

        public void Dispose() {
            lock (_waiters) {
                foreach (var tcs in _waiters) {
                    tcs.TrySetException(new ObjectDisposedException(GetType().Name));
                }
            }
        }

        public Task<IDisposable> WaitAsync(CancellationToken cancellationToken) {
            if (Interlocked.CompareExchange(ref _count, 0, 1) == 1) {
                // We got the lock
                return Task.FromResult<IDisposable>(new Holder(this, false));
            }

            lock (_waiters) {
                if (Interlocked.CompareExchange(ref _count, 0, 1) == 1) {
                    // We got the lock 
                    return Task.FromResult<IDisposable>(new Holder(this, false));
                }
                var tcs = new TaskCompletionSource<IDisposable>();
                _waiters.Add(tcs);

                if (cancellationToken.CanBeCanceled) {
                    cancellationToken.Register(() => tcs.TrySetCanceled());
                }
                return tcs.Task;
            }
        }

        public IDisposable WaitAndDispose(int milliseconds) {
            using (var cts = new CancellationTokenSource(milliseconds)) {
                try {
                    WaitAsync(cts.Token).Wait(cts.Token);
                } catch (AggregateException ae) when (ae.InnerException is OperationCanceledException) {
                }
            }
            return new Holder(this, true);
        }

        private void Release() {
            lock (_waiters) {
                Holder res = null;
                while (_waiters.Count > 0) {
                    if (res == null) {
                        res = new Holder(this, false);
                    }
                    var tcs = _waiters[0];
                    _waiters.RemoveAt(0);
                    if (tcs.TrySetResult(res)) {
                        return;
                    }
                }
                res?.Clear();
                res?.Dispose();
                Debug.Assert(Volatile.Read(ref _count) == 0);
                Interlocked.CompareExchange(ref _count, 1, 0);
                Debug.Assert(Volatile.Read(ref _count) == 1);
            }
        }

        sealed class Holder : IDisposable {
            private AsyncMutex _mutex;
            private readonly bool _dispose;

            public Holder(AsyncMutex mutex, bool dispose) {
                _mutex = mutex;
                _dispose = dispose;
            }

            internal void Clear() {
                _mutex = null;
            }

            public void Dispose() {
                if (_dispose) {
                    _mutex?.Dispose();
                } else {
                    _mutex?.Release();
                }
                GC.SuppressFinalize(this);
            }

            ~Holder() {
                Dispose();
            }
        }
    }
}
