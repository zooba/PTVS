using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis {
    public interface IAnalysisSet : ICollection<AnalysisValue>, IReadOnlyCollection<AnalysisValue> {
        void AddRange(IEnumerable<AnalysisValue> values);
        IAnalysisSet Union(IEnumerable<AnalysisValue> other);
        IAnalysisSet Clone(bool asReadOnly = false);
        bool Any();
        new int Count { get; }
        long Version { get; }
        bool SetEquals(IAnalysisSet other);
    }
}
