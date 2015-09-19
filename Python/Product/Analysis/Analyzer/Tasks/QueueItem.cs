using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    abstract class QueueItem : IReadOnlyCollection<QueueItem> {
        protected QueueItem(AnalysisState item) {
            _item = item;
        }

        public virtual ThreadPriority Priority { get { return ThreadPriority.Lowest; } }

        public int Count { get { return 1; } }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        public IEnumerator<QueueItem> GetEnumerator() { yield return this; }

        protected readonly AnalysisState _item;
        public abstract Task PerformAsync(
            PythonLanguageService analyzer,
            PythonFileContext context,
            CancellationToken cancellationToken
        );
    }
}
