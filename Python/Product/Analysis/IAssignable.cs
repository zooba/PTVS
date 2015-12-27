using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis {
    public interface IAssignable {
        IEnumerable<VariableKey> Keys { get; }
        Task AddTypeAsync(VariableKey key, IAnalysisSet values, CancellationToken cancellationToken);
        Task AddTypesAsync(IAnalysisSet values, CancellationToken cancellationToken);
        IAssignable WithSuffix(string suffix);
    }
}
