using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    abstract class QueueItem : IReadOnlyCollection<QueueItem> {
        protected QueueItem(AnalysisState item) {
            _item = item;
        }

        public int Count { get { return 1; } }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        public IEnumerator<QueueItem> GetEnumerator() { yield return this; }

        protected readonly AnalysisState _item;
        public abstract Task PerformAsync(PythonLanguageService analyzer, CancellationToken cancellationToken);
    }
}
