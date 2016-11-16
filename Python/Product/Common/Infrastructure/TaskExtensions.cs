// Visual Studio Shared Project
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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Infrastructure {
    public static class TaskExtensions {
        /// <summary>
        /// Suppresses warnings about unawaited tasks and ensures that unhandled
        /// errors will cause the process to terminate.
        /// </summary>
        public static async void DoNotWait(this Task task) {
            await task;
        }

        public static T WaitOrDefault<T>(this Task<T> task, int milliseconds) {
            if (task.Wait(milliseconds)) {
                return task.Result;
            }
            return default(T);
        }

        /// <summary>
        /// Waits for a task to complete. If an exception occurs, the exception
        /// will be raised without being wrapped in a
        /// <see cref="AggregateException"/>.
        /// </summary>
        public static void WaitAndUnwrapExceptions(this Task task) {
            task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Waits for a task to complete. If an exception occurs, the exception
        /// will be raised without being wrapped in a
        /// <see cref="AggregateException"/>.
        /// </summary>
        public static T WaitAndUnwrapExceptions<T>(this Task<T> task) {
            return task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Waits for a task to complete. If an exception occurs, the exception
        /// will be raised without being wrapped in a
        /// <see cref="AggregateException"/>.
        /// </summary>
        public static T WaitAndUnwrapExceptions<T>(this Task<T> task, Func<T> onCancel) {
            try {
                return task.GetAwaiter().GetResult();
            } catch (OperationCanceledException) {
                return onCancel();
            }
        }

        /// <summary>
        /// Silently handles the specified exception.
        /// </summary>
        public static Task SilenceException<T>(this Task task) where T : Exception {
            return task.ContinueWith(t => {
                try {
                    t.Wait();
                } catch (AggregateException ex) {
                    ex.Handle(e => e is T);
                }
            });
        }

        /// <summary>
        /// Silently handles the specified exception.
        /// </summary>
        public static Task<U> SilenceException<T, U>(this Task<U> task) where T : Exception {
            return task.ContinueWith(t => {
                try {
                    return t.Result;
                } catch (AggregateException ex) {
                    ex.Handle(e => e is T);
                    return default(U);
                }
            });
        }

        private sealed class SemaphoreLock : IDisposable {
            private SemaphoreSlim _semaphore;

            public SemaphoreLock(SemaphoreSlim semaphore) {
                _semaphore = semaphore;
            }

            public void Reset() {
                _semaphore = null;
            }

            void IDisposable.Dispose() {
                try {
                    _semaphore?.Release();
                } catch (ObjectDisposedException ex) {
                    throw new OperationCanceledException("semaphore was disposed", ex);
                } finally {
                    _semaphore = null;
                    GC.SuppressFinalize(this);
                }
            }

            ~SemaphoreLock() {
                try {
                    _semaphore?.Release();
                } catch (ObjectDisposedException) {
                }
            }
        }

        public static async Task<IDisposable> LockAsync(this SemaphoreSlim semaphore, CancellationToken cancellationToken) {
            var res = new SemaphoreLock(semaphore);
            try {
                try {
                    await semaphore.WaitAsync(cancellationToken);
                    var res2 = res;
                    res = null;
                    return res2;
                } finally {
                    res?.Reset();
                }
            } catch (ObjectDisposedException ex) {
                throw new OperationCanceledException("semaphore was disposed", ex);
            }
        }
    }
}