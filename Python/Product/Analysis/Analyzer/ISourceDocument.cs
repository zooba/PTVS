using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public interface ISourceDocument {
        Task<Stream> ReadAsync();

        string Moniker { get; }
    }
}
