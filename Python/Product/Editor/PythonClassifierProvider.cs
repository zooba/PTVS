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
using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Editor.Properties;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.PythonTools.Editor {
    /// <summary>
    /// Implements classification of text by using a ScriptEngine which supports the
    /// TokenCategorizer service.
    /// 
    /// Languages should subclass this type and override the Engine property. They 
    /// should then export the provider using MEF indicating the content type 
    /// which it is applicable to.
    /// </summary>
    [Export(typeof(IClassifierProvider))]
    [ContentType(ContentType.Name)]
    internal class PythonClassifierProvider : IClassifierProvider {
        private Dictionary<TokenCategory, IClassificationType> _categoryMap;
        private IClassificationType _comment;
        private IClassificationType _grouping;
        private IClassificationType _stringLiteral;
        private IClassificationType _keyword;
        private IClassificationType _operator;
        private IClassificationType _dot;
        private IClassificationType _comma;
        
        /// <summary>
        /// Import the classification registry to be used for getting a reference
        /// to the custom classification type later.
        /// </summary>
        [Import]
        public IClassificationTypeRegistryService _classificationRegistry = null; // Set via MEF

        [Import]
        public PythonLanguageServiceProvider _languageService = null; // Set via MEF

        #region Python Classification Type Definitions

        [Export]
        [Name(PythonPredefinedClassificationTypeNames.Grouping)]
        [BaseDefinition(PythonPredefinedClassificationTypeNames.Operator)]
        internal static ClassificationTypeDefinition GroupingClassificationDefinition = null; // Set via MEF

        [Export]
        [Name(PythonPredefinedClassificationTypeNames.Dot)]
        [BaseDefinition(PythonPredefinedClassificationTypeNames.Operator)]
        internal static ClassificationTypeDefinition DotClassificationDefinition = null; // Set via MEF

        [Export]
        [Name(PythonPredefinedClassificationTypeNames.Comma)]
        [BaseDefinition(PythonPredefinedClassificationTypeNames.Operator)]
        internal static ClassificationTypeDefinition CommaClassificationDefinition = null; // Set via MEF

        [Export]
        [Name(PythonPredefinedClassificationTypeNames.Operator)]
        [BaseDefinition(PredefinedClassificationTypeNames.Operator)]
        internal static ClassificationTypeDefinition OperatorClassificationDefinition = null; // Set via MEF

        [Export]
        [Name(PythonPredefinedClassificationTypeNames.Builtin)]
        [BaseDefinition(PredefinedClassificationTypeNames.Identifier)]
        internal static ClassificationTypeDefinition BuiltinClassificationDefinition = null; // Set via MEF

        #endregion

        public IClassifier GetClassifier(ITextBuffer buffer) {
            if (_categoryMap == null) {
                _categoryMap = FillCategoryMap(_classificationRegistry);
            }

            return buffer.Properties.GetOrCreateSingletonProperty(() => new PythonClassifier(this, buffer));
        }

        public IClassificationType Comment => _comment;
        public IClassificationType StringLiteral => _stringLiteral;
        public IClassificationType Keyword => _keyword;
        public IClassificationType Operator => _operator;
        public IClassificationType GroupingClassification => _grouping;
        public IClassificationType DotClassification => _dot;
        public IClassificationType CommaClassification => _comma;

        internal Dictionary<TokenCategory, IClassificationType> CategoryMap => _categoryMap;

        private Dictionary<TokenCategory, IClassificationType> FillCategoryMap(IClassificationTypeRegistryService registry) {
            var categoryMap = new Dictionary<TokenCategory, IClassificationType>();

            categoryMap[TokenCategory.None] = registry.GetClassificationType(PredefinedClassificationTypeNames.ExcludedCode);
            categoryMap[TokenCategory.Identifier] = registry.GetClassificationType(PredefinedClassificationTypeNames.Identifier);
            categoryMap[TokenCategory.Keyword] = _keyword = registry.GetClassificationType(PredefinedClassificationTypeNames.Keyword);
            categoryMap[TokenCategory.Operator] = _operator = registry.GetClassificationType(PythonPredefinedClassificationTypeNames.Operator);
            categoryMap[TokenCategory.StringLiteral] = _stringLiteral = registry.GetClassificationType(PredefinedClassificationTypeNames.String);
            categoryMap[TokenCategory.NumericLiteral] = registry.GetClassificationType(PredefinedClassificationTypeNames.Number);
            categoryMap[TokenCategory.Comment] = _comment = registry.GetClassificationType(PredefinedClassificationTypeNames.Comment);
            categoryMap[TokenCategory.Grouping] = _grouping = registry.GetClassificationType(PythonPredefinedClassificationTypeNames.Grouping);
            categoryMap[TokenCategory.Delimiter] = registry.GetClassificationType(PythonPredefinedClassificationTypeNames.Operator);
            categoryMap[TokenCategory.Whitespace] = registry.GetClassificationType(PredefinedClassificationTypeNames.WhiteSpace);
            _comma = registry.GetClassificationType(PythonPredefinedClassificationTypeNames.Comma);
            _dot = registry.GetClassificationType(PythonPredefinedClassificationTypeNames.Dot);

            return categoryMap;
        }
    }

    #region Editor Format Definitions

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PythonPredefinedClassificationTypeNames.Operator)]
    [Name(PythonPredefinedClassificationTypeNames.Operator)]
    [UserVisible(true)]
    [Order(After = LanguagePriority.NaturalLanguage, Before = LanguagePriority.FormalLanguage)]
    internal sealed class OperatorFormat : ClassificationFormatDefinition {
        public OperatorFormat() {
            DisplayName = Resources.OperatorClassificationType;
            // Matches "Operator"
            ForegroundColor = Colors.Black;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PythonPredefinedClassificationTypeNames.Grouping)]
    [Name(PythonPredefinedClassificationTypeNames.Grouping)]
    [UserVisible(true)]
    [Order(After = LanguagePriority.NaturalLanguage, Before = LanguagePriority.FormalLanguage)]
    internal sealed class GroupingFormat : ClassificationFormatDefinition {
        public GroupingFormat() {
            DisplayName = Resources.GroupingClassificationType;
            // Matches "Operator"
            ForegroundColor = Colors.Black;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PythonPredefinedClassificationTypeNames.Comma)]
    [Name(PythonPredefinedClassificationTypeNames.Comma)]
    [UserVisible(true)]
    [Order(After = LanguagePriority.NaturalLanguage, Before = LanguagePriority.FormalLanguage)]
    internal sealed class CommaFormat : ClassificationFormatDefinition {
        public CommaFormat() {
            DisplayName = Resources.CommaClassificationType;
            // Matches "Operator"
            ForegroundColor = Colors.Black;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PythonPredefinedClassificationTypeNames.Dot)]
    [Name(PythonPredefinedClassificationTypeNames.Dot)]
    [UserVisible(true)]
    [Order(After = LanguagePriority.NaturalLanguage, Before = LanguagePriority.FormalLanguage)]
    internal sealed class DotFormat : ClassificationFormatDefinition {
        public DotFormat() {
            DisplayName = Resources.DotClassificationType;
            // Matches "Operator"
            ForegroundColor = Colors.Black;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PythonPredefinedClassificationTypeNames.Builtin)]
    [Name(PythonPredefinedClassificationTypeNames.Builtin)]
    [UserVisible(true)]
    [Order(After = LanguagePriority.NaturalLanguage, Before = LanguagePriority.FormalLanguage)]
    internal sealed class BuiltinFormat : ClassificationFormatDefinition {
        public BuiltinFormat() {
            DisplayName = Resources.BuiltinClassificationType;
            // Matches "Keyword"
            ForegroundColor = Colors.Blue;
        }
    }

    #endregion
}
