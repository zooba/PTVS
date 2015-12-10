using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;

namespace Microsoft.PythonTools.Analysis.Values {
    public abstract class CallableValue : AnalysisValue {
        protected CallableValue(VariableKey key) : base(key) {
        }

        public override async Task<string> ToAnnotationAsync(CancellationToken cancellationToken) {
            return "Callable";
        }

        public virtual IReadOnlyList<AnalysisValue> Call(
            VariableKey self,
            IReadOnlyList<IReadOnlyList<AnalysisValue>> args,
            IReadOnlyDictionary<string, IReadOnlyList<AnalysisValue>> keywordArgs,
            Func<string, IReadOnlyList<AnalysisValue>> getVariable
        ) {
            return null;
        }

    }
}
