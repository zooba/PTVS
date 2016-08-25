using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.VisualStudio.Text;

namespace Microsoft.PythonTools.Workspace.Intellisense {
    public interface ISignatureAnalysisExtension {
        IEnumerable<ParameterResult> TryGetSignatures(SnapshotSpan text, ITrackingSpan applicableSpan);
    }
}
