// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Workspace {
    class PyprojContext {
        public const string ContextType = "CC8B54C0-4FA5-4C15-9E77-4C7C12A5E564";
        public static readonly Guid ContextGuid = new Guid(ContextType);

        private readonly string _path;
        private readonly HashSet<string> _files;
        private readonly Lazy<IReadOnlyCollection<string>> _allFiles;
        private readonly LaunchConfiguration _config;

        public PyprojContext(string projectPath, IEnumerable<string> files, LaunchConfiguration launchConfiguration) {
            _path = projectPath;
            _files = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            _allFiles = new Lazy<IReadOnlyCollection<string>>(() => _files.ToArray());
            _config = launchConfiguration;
        }

        public bool Contains(string filePath) {
            return _files.Contains(filePath);
        }

        public IReadOnlyCollection<string> AllFiles => _allFiles.Value;

        public string Name => PathUtils.GetFileOrDirectoryName(_path);
    }
}
