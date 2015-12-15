using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public static class AnalysisStateExtensions {
        public static async Task<Dictionary<string, IAnalysisSet>> GetAllTypesAsync(
            this IAnalysisState state,
            CancellationToken cancellationToken
        ) {
            var result = new Dictionary<string, IAnalysisSet>();
            var s = state as AnalysisState;
            if (s != null) {
                return await s.GetAllTypesAsync(cancellationToken);
            }

            foreach (var name in (await state.GetVariablesAsync(cancellationToken)).MaybeEnumerate()) {
                result[name] = await state.GetTypesAsync(name, cancellationToken);
            }
            return result;
        }
    }
}
