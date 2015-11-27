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
using Microsoft.PythonTools.Editor.Properties;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.PythonTools.Editor {
    [Export(typeof(IClassifierProvider)), ContentType(ContentType.Name)]
    internal class PythonAnalysisClassifierProvider : IClassifierProvider {
        [Import]
        internal IClassificationTypeRegistryService _contentTypeRegistryService = null;
        private Dictionary<string, IClassificationType> _categoryMap;

        /// <summary>
        /// Import the classification registry to be used for getting a reference
        /// to the custom classification type later.
        /// </summary>
        [Import]
        public IClassificationTypeRegistryService _classificationRegistry = null; // Set via MEF

        [Export]
        [Name(PythonPredefinedClassificationTypeNames.Class)]
        [BaseDefinition(PredefinedClassificationTypeNames.Identifier)]
        internal static ClassificationTypeDefinition ClassClassificationDefinition = null; // Set via MEF

        [Export]
        [Name(PythonPredefinedClassificationTypeNames.Function)]
        [BaseDefinition(PredefinedClassificationTypeNames.Identifier)]
        internal static ClassificationTypeDefinition FunctionClassificationDefinition = null; // Set via MEF

        [Export]
        [Name(PythonPredefinedClassificationTypeNames.Parameter)]
        [BaseDefinition(PredefinedClassificationTypeNames.Identifier)]
        internal static ClassificationTypeDefinition ParameterClassificationDefinition = null; // Set via MEF

        [Export]
        [Name(PythonPredefinedClassificationTypeNames.Module)]
        [BaseDefinition(PredefinedClassificationTypeNames.Identifier)]
        internal static ClassificationTypeDefinition ModuleClassificationDefinition = null; // Set via MEF

        public PythonAnalysisClassifierProvider() { }

        public IClassifier GetClassifier(ITextBuffer buffer) {
            if (_categoryMap == null) {
                _categoryMap = FillCategoryMap(_classificationRegistry);
            }

            PythonAnalysisClassifier res;
            if (!buffer.Properties.TryGetProperty<PythonAnalysisClassifier>(typeof(PythonAnalysisClassifier), out res) &&
                buffer.ContentType.IsOfType(ContentType.Name)) {
                res = new PythonAnalysisClassifier(this, buffer);
                buffer.Properties.AddProperty(typeof(PythonAnalysisClassifier), res);
            }

            return res;
        }

        internal Dictionary<string, IClassificationType> CategoryMap {
            get { return _categoryMap; }
        }

        private Dictionary<string, IClassificationType> FillCategoryMap(IClassificationTypeRegistryService registry) {
            var categoryMap = new Dictionary<string, IClassificationType>();

            categoryMap[PythonPredefinedClassificationTypeNames.Class] = registry.GetClassificationType(PythonPredefinedClassificationTypeNames.Class);
            categoryMap[PythonPredefinedClassificationTypeNames.Parameter] = registry.GetClassificationType(PythonPredefinedClassificationTypeNames.Parameter);
            categoryMap[PythonPredefinedClassificationTypeNames.Module] = registry.GetClassificationType(PythonPredefinedClassificationTypeNames.Module);
            categoryMap[PythonPredefinedClassificationTypeNames.Function] = registry.GetClassificationType(PythonPredefinedClassificationTypeNames.Function);
            // Include keyword for context-sensitive keywords
            categoryMap[PredefinedClassificationTypeNames.Keyword] = registry.GetClassificationType(PredefinedClassificationTypeNames.Keyword);

            return categoryMap;
        }
    }

    #region Editor Format Definitions

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PythonPredefinedClassificationTypeNames.Class)]
    [Name(PythonPredefinedClassificationTypeNames.Class)]
    [UserVisible(true)]
    [Order(After = LanguagePriority.NaturalLanguage, Before = LanguagePriority.FormalLanguage)]
    internal sealed class ClassFormat : ClassificationFormatDefinition {
        public ClassFormat() {
            DisplayName = Resources.ClassClassificationType;
            // Matches "C++ User Types"
            ForegroundColor = Color.FromArgb(255, 43, 145, 175);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PythonPredefinedClassificationTypeNames.Module)]
    [Name(PythonPredefinedClassificationTypeNames.Module)]
    [UserVisible(true)]
    [Order(After = LanguagePriority.NaturalLanguage, Before = LanguagePriority.FormalLanguage)]
    internal sealed class ModuleFormat : ClassificationFormatDefinition {
        public ModuleFormat() {
            DisplayName = Resources.ModuleClassificationType;
            // Matches "C++ Macros"
            ForegroundColor = Color.FromArgb(255, 111, 0, 138);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PythonPredefinedClassificationTypeNames.Parameter)]
    [Name(PythonPredefinedClassificationTypeNames.Parameter)]
    [UserVisible(true)]
    [Order(After = LanguagePriority.NaturalLanguage, Before = LanguagePriority.FormalLanguage)]
    internal sealed class ParameterFormat : ClassificationFormatDefinition {
        public ParameterFormat() {
            DisplayName = Resources.ParameterClassificationType;
            // Matches "C++ Parameters"
            ForegroundColor = Color.FromArgb(255, 128, 128, 128);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = PythonPredefinedClassificationTypeNames.Function)]
    [Name(PythonPredefinedClassificationTypeNames.Function)]
    [UserVisible(true)]
    [Order(After = LanguagePriority.NaturalLanguage, Before = LanguagePriority.FormalLanguage)]
    internal sealed class FunctionFormat : ClassificationFormatDefinition {
        public FunctionFormat() {
            DisplayName = Resources.FunctionClassificationType;
            // Matches "C++ Functions"
            ForegroundColor = Colors.Black;
        }
    }

    #endregion
}
