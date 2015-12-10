using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis {
    public static class AnalysisValueExtensions {
        public static async Task<string> ToAnnotationAsync(
            this IEnumerable<AnalysisValue> values,
            CancellationToken cancellationToken
        ) {
            var names = await values.AsAnnotationsAsync(cancellationToken);
            return string.Join(", ", names.Ordered());
        }

        public static async Task<IReadOnlyCollection<string>> AsAnnotationsAsync(
            this IEnumerable<AnalysisValue> values,
            CancellationToken cancellationToken
        ) {
            var names = new HashSet<string>();
            foreach (var t in values) {
                names.Add(await t.ToAnnotationAsync(cancellationToken));
            }
            return names;
        }

        private static void Add(ref IAnalysisSet set, AnalysisValue value) {
            if (set == null) {
                set = value;
            } else if (set.IsReadOnly) {
                set = set.Clone(false);
                set.Add(value);
            } else {
                set.Add(value);
            }
        }

        private static void Add(ref IAnalysisSet set, IEnumerable<AnalysisValue> value) {
            if (set == null) {
                set = new AnalysisSet(value).Trim();
            } else if (set.IsReadOnly) {
                set = set.Clone(false);
                set.AddRange(value);
            } else {
                set.AddRange(value);
            }
        }
        public static async Task<IAnalysisSet> GetAttribute(
            this IEnumerable<AnalysisValue> values, 
            string attribute,
            CancellationToken cancellationToken
        ) {
            IAnalysisSet result = null;
            foreach (var t in values) {
                Add(ref result, await t.GetAttribute(attribute, cancellationToken));
            }
            return result;
        }

        public static async Task<IAnalysisSet> Call(
            this IEnumerable<AnalysisValue> values, 
            IReadOnlyList<VariableKey> args,
            IReadOnlyDictionary<string, VariableKey> keywordArgs,
            CancellationToken cancellationToken
        ) {
            IAnalysisSet result = null;
            foreach (var t in values) {
                Add(ref result, await t.Call(args, keywordArgs, cancellationToken));
            }
            return result;
        }

        public static IAnalysisSet ToSet(this IEnumerable<AnalysisValue> values) {
            return new AnalysisSet(values).Trim();
        }
    }
}
