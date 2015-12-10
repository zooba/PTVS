using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis {
    public interface IAnalysisSet : ICollection<AnalysisValue>, IReadOnlyCollection<AnalysisValue> {
        void AddRange(IEnumerable<AnalysisValue> values);
        IAnalysisSet Clone(bool asReadOnly = false);
        new int Count { get; }
        long Version { get; }
        bool SetEquals(IAnalysisSet other);
    }
}
