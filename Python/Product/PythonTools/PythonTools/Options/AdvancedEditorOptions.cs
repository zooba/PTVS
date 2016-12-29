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
using Microsoft.PythonTools.Intellisense;

namespace Microsoft.PythonTools.Options {
    public sealed class AdvancedEditorOptions {
        private readonly PythonToolsService _service;

        private const string Category = "Advanced";

        private const string EnterCommitsSetting = "EnterCommits";
        private const string IntersectMembersSetting = "IntersectMembers";
        private const string NewLineAtEndOfWordSetting = "NewLineAtEndOfWord";
        private const string CompletionCommittedBySetting = "CompletionCommittedBy";
        private const string EnterOutlingModeOnOpenSetting = "EnterOutlingModeOnOpen";
        private const string PasteRemovesReplPromptsSetting = "PasteRemovesReplPrompts";
        private const string FilterCompletionsSetting = "FilterCompletions";
        private const string SearchModeSetting = "SearchMode";
        private const string ColorNamesSetting = "ColorNames";
        private const string ColorNamesWithAnalysisSetting = "ColorNamesWithAnalysis";
        private const string AutoListIdentifiersSetting = "AutoListIdentifiers";

        private const string _oldDefaultCompletionChars = "{}[]().,:;+-*/%&|^~=<>#'\"\\";
        private const string _defaultCompletionChars = "{}[]().,:;+-*/%&|^~=<>#@\\";

        internal AdvancedEditorOptions(PythonToolsService service) {
            _service = service;
        }

        public void Load() {
            EnterCommitsIntellisense = _service.LoadBool(EnterCommitsSetting, Category) ?? true;
            IntersectMembers = _service.LoadBool(IntersectMembersSetting, Category) ?? false;
            AddNewLineAtEndOfFullyTypedWord = _service.LoadBool(NewLineAtEndOfWordSetting, Category) ?? false;
            CompletionCommittedBy = _service.LoadString(CompletionCommittedBySetting, Category) ?? _defaultCompletionChars;
            if (CompletionCommittedBy == _oldDefaultCompletionChars) {
                CompletionCommittedBy = _defaultCompletionChars;
                _service.SaveString(CompletionCommittedBySetting, Category, CompletionCommittedBy);
            }
            EnterOutliningModeOnOpen = _service.LoadBool(EnterOutlingModeOnOpenSetting, Category) ?? true;
            PasteRemovesReplPrompts = _service.LoadBool(PasteRemovesReplPromptsSetting, Category) ?? true;
            FilterCompletions = _service.LoadBool(FilterCompletionsSetting, Category) ?? true;
            SearchMode = _service.LoadEnum<FuzzyMatchMode>(SearchModeSetting, Category) ?? FuzzyMatchMode.Default;
            ColorNames = _service.LoadBool(ColorNamesSetting, Category) ?? true;
            ColorNamesWithAnalysis = _service.LoadBool(ColorNamesWithAnalysisSetting, Category) ?? true;
            AutoListIdentifiers = _service.LoadBool(AutoListIdentifiersSetting, Category) ?? true;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Save() {
            _service.SaveBool(EnterCommitsSetting, Category, EnterCommitsIntellisense);
            _service.SaveBool(IntersectMembersSetting, Category, IntersectMembers);
            _service.SaveBool(NewLineAtEndOfWordSetting, Category, AddNewLineAtEndOfFullyTypedWord);
            _service.SaveString(CompletionCommittedBySetting, Category, CompletionCommittedBy);
            _service.SaveBool(EnterOutlingModeOnOpenSetting, Category, EnterOutliningModeOnOpen);
            _service.SaveBool(PasteRemovesReplPromptsSetting, Category, PasteRemovesReplPrompts);
            _service.SaveBool(FilterCompletionsSetting, Category, FilterCompletions);
            _service.SaveEnum(SearchModeSetting, Category, SearchMode);
            _service.SaveBool(ColorNamesSetting, Category, ColorNames);
            _service.SaveBool(ColorNamesWithAnalysisSetting, Category, ColorNamesWithAnalysis);
            _service.SaveBool(AutoListIdentifiersSetting, Category, AutoListIdentifiers);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Reset() {
            EnterCommitsIntellisense = true;
            IntersectMembers = true;
            AddNewLineAtEndOfFullyTypedWord = false;
            CompletionCommittedBy = _defaultCompletionChars;
            EnterOutliningModeOnOpen = true;
            PasteRemovesReplPrompts = true;
            FilterCompletions = true;
            SearchMode = FuzzyMatchMode.Default;
            ColorNames = true;
            ColorNamesWithAnalysis = true;
            AutoListIdentifiers = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Changed;

        public string CompletionCommittedBy {
            get;
            set;
        }

        public bool EnterCommitsIntellisense {
            get;
            set;
        }

        public bool AddNewLineAtEndOfFullyTypedWord {
            get;
            set;
        }

        public bool FilterCompletions {
            get;
            set;
        }

        public bool IntersectMembers {
            get;
            set;
        }

        public FuzzyMatchMode SearchMode {
            get;
            set;
        }

        public bool ColorNames {
            get;
            set;
        }

        public bool ColorNamesWithAnalysis {
            get;
            set;
        }

        public bool EnterOutliningModeOnOpen {
            get;
            set;
        }

        public bool PasteRemovesReplPrompts {
            get;
            set;
        }

        public bool AutoListMembers {
            get {
                return _service.LangPrefs.AutoListMembers;
            }
            set {
                var prefs = _service.GetLanguagePreferences();
                var val = value ? 1u : 0u;
                if (prefs.fAutoListMembers != val) {
                    prefs.fAutoListMembers = val;
                    _service.SetLanguagePreferences(prefs);
                }
            }
        }

        public bool HideAdvancedMembers {
            get {
                return _service.LangPrefs.HideAdvancedMembers;
            }
            set {
                var prefs = _service.GetLanguagePreferences();
                var val = value ? 1u : 0u;
                if (prefs.fHideAdvancedAutoListMembers != val) {
                    prefs.fHideAdvancedAutoListMembers = val;
                    _service.SetLanguagePreferences(prefs);
                }
            }
        }

        public bool AutoListIdentifiers {
            get;
            set;
        }
    }
}
