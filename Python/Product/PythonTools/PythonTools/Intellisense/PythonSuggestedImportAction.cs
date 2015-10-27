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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.PythonTools.Analysis;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace Microsoft.PythonTools.Intellisense {
    class PythonSuggestedImportAction : ISuggestedAction, IComparable<PythonSuggestedImportAction> {
        private readonly PythonSuggestedActionsSource _source;
        private readonly string _name;
        private readonly string _fromModule;
        private readonly ITextBuffer _buffer;

        public PythonSuggestedImportAction(PythonSuggestedActionsSource source, ITextBuffer buffer, ExportedMemberInfo import) {
            _source = source;
            _fromModule = import.FromName;
            _name = import.ImportName;
            _buffer = buffer;
        }

        public IEnumerable<SuggestedActionSet> ActionSets {
            get {
                return Enumerable.Empty<SuggestedActionSet>();
            }
        }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken) {
            return Task.FromResult(ActionSets);
        }

        public bool HasActionSets {
            get { return false; }
        }

        public string DisplayText {
            get {
                return MissingImportAnalysis.MakeImportCode(_fromModule, _name)
                    .Replace("_", "__");
            }
        }

        public string IconAutomationText {
            get {
                return null;
            }
        }

        public ImageMoniker IconMoniker {
            get {
                return default(ImageMoniker);
            }
        }

        public ImageSource IconSource {
            get {
                // TODO: Convert from IconMoniker
                return null;
            }
        }

        public string InputGestureText {
            get {
                return null;
            }
        }

        public void Dispose() { }

        public object GetPreview(CancellationToken cancellationToken) {
            return null;
        }

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken) {
            return Task.FromResult<object>(null);
        }

        public bool HasPreview {
            get { return false; }
        }

        public void Invoke(CancellationToken cancellationToken) {
            Debug.Assert(!string.IsNullOrEmpty(_name));

            MissingImportAnalysis.AddImport(
                _source._provider,
                _buffer,
                _source._view,
                _fromModule,
                _name
            );
        }

        public bool TryGetTelemetryId(out Guid telemetryId) {
            telemetryId = Guid.Empty;
            return false;
        }

        public override bool Equals(object obj) {
            var other = obj as PythonSuggestedImportAction;
            if (other == null) {
                return false;
            }
            return DisplayText.Equals(other.DisplayText);
        }

        public override int GetHashCode() {
            return DisplayText.GetHashCode();
        }

        public int CompareTo(PythonSuggestedImportAction other) {
            if (other == null) {
                return -1;
            }

            // Sort from ... import before import ...
            if (!string.IsNullOrEmpty(_fromModule)) {
                if (string.IsNullOrEmpty(other._fromModule)) {
                    return -1;
                }
            } else if (!string.IsNullOrEmpty(other._fromModule)) {
                return 1;
            }

            var key1 = _fromModule ?? _name ?? "";
            var key2 = other._fromModule ?? other._name ?? "";

            // Name with fewer dots sorts first
            var dotCount1 = key1.Count(c => c == '.');
            var dotCount2 = key2.Count(c => c == '.');
            int r = dotCount1.CompareTo(dotCount2);
            if (r != 0) {
                return r;
            }

            // Shorter name sorts first
            r = key1.Length.CompareTo(key2.Length);
            if (r != 0) {
                return r;
            }

            // Keys sort alphabetically
            r = key1.CompareTo(key2);
            if (r != 0) {
                return r;
            }

            // Sort by display text
            return (DisplayText ?? "").CompareTo(other.DisplayText ?? "");
        }
    }
}
