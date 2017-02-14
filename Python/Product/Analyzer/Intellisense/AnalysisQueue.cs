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
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Infrastructure;

namespace Microsoft.PythonTools.Intellisense {
    /// <summary>
    /// Provides a single threaded analysis queue.  Items can be enqueued into the
    /// analysis at various priorities.  
    /// </summary>
    sealed class AnalysisQueue : IDisposable {
        private readonly Thread _workThread;
        private readonly AutoResetEvent _workEvent;
        private readonly OutOfProcProjectAnalyzer _analyzer;
        private readonly object _queueLock = new object();
        private readonly List<IAnalyzable>[] _queue;
        private readonly HashSet<IGroupableAnalysisProject> _enqueuedGroups = new HashSet<IGroupableAnalysisProject>();
        private TaskScheduler _scheduler;
        private CancellationTokenSource _cancel;
        private bool _isAnalyzing;
        private int _analysisPending;

        private const int PriorityCount = (int)AnalysisPriority.High + 1;

        internal AnalysisQueue(OutOfProcProjectAnalyzer analyzer) {
            _workEvent = new AutoResetEvent(false);
            _cancel = new CancellationTokenSource();
            _analyzer = analyzer;

            _queue = new List<IAnalyzable>[PriorityCount];
            for (int i = 0; i < PriorityCount; i++) {
                _queue[i] = new List<IAnalyzable>();
            }

            _workThread = new Thread(Worker);
            _workThread.Name = "Python Analysis Queue";
            _workThread.Priority = ThreadPriority.BelowNormal;
            _workThread.IsBackground = true;

            // start the thread, wait for our synchronization context to be created
            using (AutoResetEvent threadStarted = new AutoResetEvent(false)) {
                _workThread.Start(threadStarted);
                threadStarted.WaitOne();
            }
        }

        public TaskScheduler Scheduler {
            get {
                return _scheduler;
            }
        }

        public void Enqueue(IAnalyzable item, AnalysisPriority priority) {
            int iPri = (int)priority;

            if (iPri < 0 || iPri > _queue.Length) {
                throw new ArgumentException("priority");
            }

            lock (_queueLock) {
                // see if we have the item in the queue anywhere...
                for (int i = 0; i < _queue.Length; i++) {
                    if (_queue[i].Remove(item)) {
                        Interlocked.Decrement(ref _analysisPending);

                        AnalysisPriority oldPri = (AnalysisPriority)i;

                        if (oldPri > priority) {
                            // if it was at a higher priority then our current
                            // priority go ahead and raise the new entry to our
                            // old priority
                            priority = oldPri;
                        }

                        break;
                    }
                }

                // enqueue the work item
                Interlocked.Increment(ref _analysisPending);
                if (priority == AnalysisPriority.High) {
                    // always try and process high pri items immediately
                    _queue[iPri].Insert(0, item);
                } else {
                    _queue[iPri].Add(item);
                }
                try {
                    _workEvent.Set();
                } catch (ObjectDisposedException) {
                    // Queue was closed while we were running
                }
            }
        }

        public void Stop() {
            try {
                _cancel.Cancel();
            } catch (ObjectDisposedException) {
            }
            if (_workThread.IsAlive) {
                try {
                    _workEvent.Set();
                } catch (ObjectDisposedException) {
                }
                if (!_workThread.Join(TimeSpan.FromSeconds(5.0))) {
                    Trace.TraceWarning("Failed to wait for worker thread to terminate");
                }
            }
        }

        public event EventHandler AnalysisStarted;

        public bool IsAnalyzing {
            get {
                lock (_queueLock) {
                    return _isAnalyzing || _analysisPending > 0;
                }
            }
        }

        public int AnalysisPending {
            get {
                return _analysisPending;
            }
        }

        #region IDisposable Members

        public void Dispose() {
            Stop();
            _workEvent.Dispose();
            _cancel.Dispose();
        }

        #endregion

        private IAnalyzable GetNextItem(out AnalysisPriority priority) {
            for (int i = PriorityCount - 1; i >= 0; i--) {
                if (_queue[i].Count > 0) {
                    var res = _queue[i][0];
                    _queue[i].RemoveAt(0);
                    Interlocked.Decrement(ref _analysisPending);
                    priority = (AnalysisPriority)i;
                    return res;
                }
            }
            priority = AnalysisPriority.None;
            return null;
        }

        private void Worker(object threadStarted) {
            try {
                SynchronizationContext.SetSynchronizationContext(new AnalysisSynchronizationContext(this));
                _scheduler = TaskScheduler.FromCurrentSynchronizationContext();
            } finally {
                ((AutoResetEvent)threadStarted).Set();
            }

            AnalysisStarted?.Invoke(this, EventArgs.Empty);
            _isAnalyzing = true;

            while (!_cancel.IsCancellationRequested) {
                IAnalyzable workItem;

                AnalysisPriority pri;
                lock (_queueLock) {
                    workItem = GetNextItem(out pri);
                }

                if (workItem != null) {
                    try {
                        var groupable = workItem as IGroupableAnalysisProjectEntry;
                        if (groupable != null) {
                            bool added = _enqueuedGroups.Add(groupable.AnalysisGroup);
                            if (added) {
                                Enqueue(new GroupAnalysis(groupable.AnalysisGroup, this), pri);
                            }

                            groupable.Analyze(_cancel.Token, true);
                        } else {
                            workItem.Analyze(_cancel.Token);
                        }
                    } catch (Exception ex) {
                        if (ex.IsCriticalException() || System.Diagnostics.Debugger.IsAttached) {
                            throw;
                        }
                        _analyzer.ReportUnhandledException(ex);
                        _cancel.Cancel();
                    }
                } else if (!_workEvent.WaitOne(50)) {
                    // Short wait for activity before raising the event.
                    _isAnalyzing = false;
                    var evt1 = AnalysisComplete;
                    if (evt1 != null) {
                        ThreadPool.QueueUserWorkItem(_ => evt1(this, EventArgs.Empty));
                    }
                    WaitHandle.SignalAndWait(_analyzer.QueueActivityEvent, _workEvent);
                    var evt2 = AnalysisStarted;
                    if (evt2 != null) {
                        ThreadPool.QueueUserWorkItem(_ => evt2(this, EventArgs.Empty));
                    }
                    _isAnalyzing = true;
                }
            }
            _isAnalyzing = false;

            if (_cancel.IsCancellationRequested) {
                AnalysisAborted?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler AnalysisComplete;

        public event EventHandler AnalysisAborted;

        sealed class GroupAnalysis : IAnalyzable {
            private readonly IGroupableAnalysisProject _project;
            private readonly AnalysisQueue _queue;

            public GroupAnalysis(IGroupableAnalysisProject project, AnalysisQueue queue) {
                _project = project;
                _queue = queue;
            }

            #region IAnalyzable Members

            public void Analyze(CancellationToken cancel) {
                _queue._enqueuedGroups.Remove(_project);
                _project.AnalyzeQueuedEntries(cancel);
            }

            #endregion
        }
    }
}
