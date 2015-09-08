using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Microsoft.PythonTools.Editor {
    interface IPythonEditorOptions {
        vsIndentStyle IndentMode { get; }
    }
}
