using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.PythonTools.Editor {
    static class ContentType {
        public const string Name = "Python";

        [Export]
        [Name(Name)]
        [BaseDefinition("code")]
        internal static ContentTypeDefinition ContentTypeDefinition = null;

        [Export]
        [FileExtension(".py")]
        [ContentType(Name)]
        internal static FileExtensionToContentTypeDefinition PyFileExtensionDefinition = null;
    }
}
