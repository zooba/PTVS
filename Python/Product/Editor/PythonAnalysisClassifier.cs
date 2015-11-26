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
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Editor {
    struct CachedClassification {
        public ITrackingSpan Span;
        public string Classification;

        public CachedClassification(ITrackingSpan span, string classification) {
            Span = span;
            Classification = classification;
        }
    }

    /// <summary>
    /// Provides classification based upon the AST and analysis.
    /// </summary>
    /// <summary>
    /// Provides classification based upon the DLR TokenCategory enum.
    /// </summary>
    internal sealed class PythonAnalysisClassifier : IClassifier, IDisposable {
        private readonly PythonAnalysisClassifierProvider _provider;
        private readonly ITextBuffer _buffer;

        private IVsTask _currentTask;

        private readonly object _classificationsLock = new object();
        private ITextVersion _classificationsVersion;
        private List<List<CachedClassification>> _classifications;

        internal PythonAnalysisClassifier(PythonAnalysisClassifierProvider provider, ITextBuffer buffer) {
            _provider = provider;
            _buffer = buffer;
            _classifications = new List<List<CachedClassification>>();

            _buffer.Changed += Buffer_Changed;
            _buffer.ContentTypeChanged += BufferContentTypeChanged;

            BeginUpdateClassifications(_buffer.CurrentSnapshot);
        }

        private void Buffer_Changed(object sender, TextContentChangedEventArgs e) {
            BeginUpdateClassifications(e.After);
        }

        internal void BeginUpdateClassifications(ITextSnapshot snapshot) {
            var task = ThreadHelper.JoinableTaskFactory.RunAsyncAsVsTask(
                VsTaskRunContext.UIThreadBackgroundPriority,
                ct => UpdateClassifications(_buffer.CurrentSnapshot, ct)
            );
            task.Description = GetType().FullName;
            var oldTask = Interlocked.Exchange(ref _currentTask, task);
            if (oldTask != null) {
                oldTask.Cancel();
            }
        }

        private async Task<bool> UpdateClassifications(ITextSnapshot snapshot, CancellationToken cancellationToken) {
            var buffer = snapshot.TextBuffer;
            var version = snapshot.Version;

            ISourceDocument document;
            PythonFileContext context;
            PythonLanguageService analyzer;

            if (!buffer.Properties.TryGetProperty(typeof(ISourceDocument), out document) ||
                !buffer.Properties.TryGetProperty(typeof(PythonFileContext), out context) ||
                !buffer.Properties.TryGetProperty(typeof(PythonLanguageService), out analyzer)) {
                return false;
            }

            var item = await analyzer.GetItemTokenAsync(context, document.Moniker, false, cancellationToken);
            if (item == null) {
                return false;
            }

            await analyzer.WaitForUpdateAsync(item, cancellationToken);

            var ast = await analyzer.GetAstAsync(item, cancellationToken);

            //var start = e.Changes.Min(c => c.NewSpan.Start);
            //var end = e.Changes.Max(c => c.NewSpan.End);
            //var span = new SnapshotSpan(e.After, start, end - start);
            var span = new SnapshotSpan(snapshot, 0, snapshot.Length);

            var walker = new ClassifierWalker(ast, analyzer, span.Snapshot, _provider.CategoryMap);
            ast.Walk(walker);
            cancellationToken.ThrowIfCancellationRequested();
            
            bool changed = false;
            lock (_classificationsLock) {
                if (_classificationsVersion == null ||
                    _classificationsVersion.VersionNumber < version.VersionNumber) {
                    _classificationsVersion = version;
                    _classifications = walker.Spans;
                    changed = true;
                }
            }

            if (changed) {
                ClassificationChanged?.Invoke(this, new ClassificationChangedEventArgs(span));
            }
            return changed;
        }

        void IDisposable.Dispose() {
            _buffer.Changed -= Buffer_Changed;
            _buffer.ContentTypeChanged -= BufferContentTypeChanged;
        }

        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

        /// <summary>
        /// This method classifies the given snapshot span.
        /// </summary>
        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span) {
            List<List<CachedClassification>> classifications;
            lock (_classificationsLock) {
                classifications = _classifications;
            }

            var map = CategoryMap;
            var result = new List<ClassificationSpan>();
            int lastLine = span.End.GetContainingLine().LineNumber;
            for (int line = span.Start.GetContainingLine().LineNumber; line <= lastLine; ++line) {
                foreach (var cs in classifications.ElementAtOrDefault(line).MaybeEnumerate()) {
                    var s = cs.Span.GetSpan(span.Snapshot);
                    IClassificationType ct;
                    if (s.OverlapsWith(span) && map.TryGetValue(cs.Classification, out ct)) {
                        result.Add(new ClassificationSpan(s, ct));
                    }
                }
            }

            return result;
        }

        public PythonAnalysisClassifierProvider Provider => _provider;
        private Dictionary<string, IClassificationType> CategoryMap => _provider.CategoryMap;

        private void BufferContentTypeChanged(object sender, ContentTypeChangedEventArgs e) {
            _buffer.Changed -= Buffer_Changed;
            _buffer.ContentTypeChanged -= BufferContentTypeChanged;
            _buffer.Properties.RemoveProperty(typeof(PythonClassifier));
        }
    }

    internal static partial class ClassifierExtensions {
        public static PythonAnalysisClassifier GetPythonAnalysisClassifier(this ITextBuffer buffer) {
            PythonAnalysisClassifier res;
            if (buffer.Properties.TryGetProperty<PythonAnalysisClassifier>(typeof(PythonAnalysisClassifier), out res)) {
                return res;
            }
            return null;
        }
    }

    class ClassifierWalker : PythonWalker {
        class StackData {
            public readonly string Name;
            public readonly HashSet<string> Parameters;
            public readonly HashSet<string> Functions;
            public readonly HashSet<string> Types;
            public readonly HashSet<string> Modules;
            public readonly List<Tuple<string, SourceSpan>> Names;
            public readonly StackData Previous;

            public StackData(string name, StackData previous) {
                Name = name;
                Previous = previous;
                Parameters = new HashSet<string>();
                Functions = new HashSet<string>();
                Types = new HashSet<string>();
                Modules = new HashSet<string>();
                Names = new List<Tuple<string, SourceSpan>>();
            }

            public IEnumerable<StackData> EnumerateTowardsGlobal {
                get {
                    for (var sd = this; sd != null; sd = sd.Previous) {
                        yield return sd;
                    }
                }
            }
        }
        
        private readonly PythonAst _ast;
        private readonly PythonLanguageService _analyzer;
        private readonly ITextSnapshot _snapshot;
        private readonly Dictionary<string, IClassificationType> _formatMap;
        private StackData _head;
        public readonly List<List<CachedClassification>> Spans;

        public ClassifierWalker(
            PythonAst ast,
            PythonLanguageService analyzer,
            ITextSnapshot snapshot,
            Dictionary<string, IClassificationType> formatMap
        ) {
            _ast = ast;
            _analyzer = analyzer;
            _snapshot = snapshot;
            _formatMap = formatMap;
            Spans = new List<List<CachedClassification>>();
        }

        private void AddSpan(Tuple<string, SourceSpan> node, string type) {
            var span = node.Item2;
            int lineNo = span.Start.Line;
            var existing = lineNo < Spans.Count ? Spans[lineNo] : null;
            if (existing == null) {
                while (lineNo >= Spans.Count) {
                    Spans.Add(null);
                }
                Spans[lineNo] = existing = new List<CachedClassification>();
            }
            existing.Add(new CachedClassification(
                _snapshot.CreateTrackingSpan(span.Start.Index, span.Length, SpanTrackingMode.EdgeExclusive),
                type
            ));
        }

        private void BeginScope(string name = null) {
            if (_head != null) {
                if (name == null) {
                    name = _head.Name;
                } else if (_head.Name != null) {
                    name = _head.Name + "." + name;
                }
            }
            _head = new StackData(name, _head);
        }

        private void AddParameter(Parameter node) {
            Debug.Assert(_head != null);
            _head.Parameters.Add(node.Name);
            _head.Names.Add(Tuple.Create(node.Name, node.NameExpression.Span));
        }

        private void AddParameter(Node node) {
            NameExpression name;
            TupleExpression tuple;
            Debug.Assert(_head != null);
            if ((name = node as NameExpression) != null) {
                _head.Parameters.Add(name.Name);
            } else if ((tuple = node as TupleExpression) != null) {
                foreach (var expr in tuple.Items) {
                    AddParameter(expr);
                }
            } else {
                Trace.TraceWarning("Unable to find parameter in {0}", node);
            }
        }

        public override bool Walk(NameExpression node) {
            _head.Names.Add(Tuple.Create(node.Name, node.Span));
            return base.Walk(node);
        }

        private static string GetFullName(MemberExpression expr) {
            var ne = expr.Expression as NameExpression;
            if (ne != null) {
                return ne.Name + "." + expr.Name ?? string.Empty;
            }
            var me = expr.Expression as MemberExpression;
            if (me != null) {
                var baseName = GetFullName(me);
                if (baseName == null) {
                    return null;
                }
                return baseName + "." + expr.Name ?? string.Empty;
            }
            return null;
        }

        public override bool Walk(MemberExpression node) {
            var fullname = GetFullName(node);
            if (fullname != null) {
                _head.Names.Add(Tuple.Create(fullname, node.NameExpression.Span));
            }
            return base.Walk(node);
        }

        public override bool Walk(DottedName node) {
            if ((node.Names?.Count ?? 0) > 0) {
                string totalName = "";
                foreach (var name in node.Names) {
                    _head.Names.Add(Tuple.Create(totalName + name.Name, new SourceSpan(node.Span.Start, name.Span.End)));
                    totalName += name.Name + ".";
                }
            }
            return base.Walk(node);
        }

        private string ClassifyName(Tuple<string, SourceSpan> node) {
            var name = node.Item1;
            foreach (var sd in _head.EnumerateTowardsGlobal) {
                if (sd.Parameters.Contains(name)) {
                    return PythonPredefinedClassificationTypeNames.Parameter;
                } else if (sd.Functions.Contains(name)) {
                    return PythonPredefinedClassificationTypeNames.Function;
                } else if (sd.Types.Contains(name)) {
                    return PythonPredefinedClassificationTypeNames.Class;
                } else if (sd.Modules.Contains(name)) {
                    return PythonPredefinedClassificationTypeNames.Module;
                }
            }

            //if (_analysis != null) {
            //    var memberType = PythonMemberType.Unknown;
            //    lock (_analysis) {
            //        memberType = _analysis
            //            .GetValuesByIndex(name, node.Item2.Start)
            //            .Select(v => v.MemberType)
            //            .DefaultIfEmpty(PythonMemberType.Unknown)
            //            .Aggregate((a, b) => a == b ? a : PythonMemberType.Unknown);
            //    }
            //
            //    if (memberType == PythonMemberType.Module) {
            //        return PythonPredefinedClassificationTypeNames.Module;
            //    } else if (memberType == PythonMemberType.Class) {
            //        return PythonPredefinedClassificationTypeNames.Class;
            //    } else if (memberType == PythonMemberType.Function || memberType == PythonMemberType.Method) {
            //        return PythonPredefinedClassificationTypeNames.Function;
            //    }
            //}

            return null;
        }

        private void EndScope(bool mergeNames) {
            var sd = _head;
            foreach (var node in sd.Names) {
                var classificationName = ClassifyName(node);
                if (classificationName != null) {
                    AddSpan(node, classificationName);
                    if (mergeNames && sd.Previous != null) {
                        if (classificationName == PythonPredefinedClassificationTypeNames.Module) {
                            sd.Previous.Modules.Add(sd.Name + "." + node.Item1);
                        } else if (classificationName == PythonPredefinedClassificationTypeNames.Class) {
                            sd.Previous.Types.Add(sd.Name + "." + node.Item1);
                        } else if (classificationName == PythonPredefinedClassificationTypeNames.Function) {
                            sd.Previous.Functions.Add(sd.Name + "." + node.Item1);
                        }
                    }
                }
            }
            _head = sd.Previous;
        }

        public override bool Walk(PythonAst node) {
            Debug.Assert(_head == null);
            _head = new StackData(string.Empty, null);
            return base.Walk(node);
        }

        public override void PostWalk(PythonAst node) {
            EndScope(false);
            Debug.Assert(_head == null);
            base.PostWalk(node);
        }

        public override bool Walk(ClassDefinition node) {
            Debug.Assert(_head != null);
            _head.Types.Add(node.NameExpression.Name);
            node.NameExpression.Walk(this);
            BeginScope(node.NameExpression.Name);
            return base.Walk(node);
        }

        public override bool Walk(FunctionDefinition node) {
            //if (node.IsAsync) {
            //    AddSpan(Tuple.Create("", new Span(node.StartIndex, 5)), PredefinedClassificationTypeNames.Keyword);
            //}

            Debug.Assert(_head != null);
            _head.Functions.Add(node.NameExpression.Name);
            node.NameExpression.Walk(this);
            BeginScope();
            return base.Walk(node);
        }

        public override bool Walk(DictionaryComprehension node) {
            BeginScope();
            return base.Walk(node);
        }

        public override bool Walk(ListComprehension node) {
            BeginScope();
            return base.Walk(node);
        }

        public override bool Walk(GeneratorExpression node) {
            BeginScope();
            return base.Walk(node);
        }

        public override bool Walk(ComprehensionFor node) {
            AddParameter(node.Left);
            return base.Walk(node);
        }

        public override bool Walk(Parameter node) {
            AddParameter(node);
            return base.Walk(node);
        }

        public override bool Walk(ImportStatement node) {
            Debug.Assert(_head != null);
            foreach(var name in node.Names.MaybeEnumerate()) {
                AsExpression asName;
                DottedName importName;
                if ((asName = name.Expression as AsExpression) != null &&
                    ((importName = asName.Expression as DottedName) != null)) {
                    foreach (var n in importName.Names.MaybeEnumerate()) {
                        // Only want to highlight this instance of the
                        // name, since it isn't going to be bound in the
                        // rest of the module.
                        AddSpan(Tuple.Create(n.Name, n.Span), PythonPredefinedClassificationTypeNames.Module);
                    }
                    _head.Modules.Add(asName.Name.Name);
                } else if ((importName = name.Expression as DottedName) != null) {
                    foreach (var n in importName.Names.MaybeEnumerate()) {
                        _head.Modules.Add(n.Name);
                        _head.Names.Add(Tuple.Create(n.Name, n.Span));
                    }
                }
            }
            return base.Walk(node);
        }

        public override bool Walk(FromImportStatement node) {
            Debug.Assert(_head != null);
            foreach (var name in node.Root?.Names.MaybeEnumerate()) {
                if (!string.IsNullOrEmpty(name?.Name)) {
                    AddSpan(Tuple.Create(name.Name, name.Span), PythonPredefinedClassificationTypeNames.Module);
                }
            }
            foreach (var name in node.Names.MaybeEnumerate()) {
                AsExpression asName;
                NameExpression importName;
                if ((asName = name.Expression as AsExpression) != null &&
                    (importName = asName.Expression as NameExpression) != null) {
                    _head.Names.Add(Tuple.Create(asName.Name.Name, asName.Name.Span));
                    AddSpan(Tuple.Create(importName.Name, importName.Span), PythonPredefinedClassificationTypeNames.Module);
                } else if ((importName = name.Expression as NameExpression) != null) {
                    _head.Names.Add(Tuple.Create(importName.Name, importName.Span));
                }
            }
            return base.Walk(node);
        }



        public override void PostWalk(ClassDefinition node) {
            EndScope(true);
            Debug.Assert(_head != null);
            base.PostWalk(node);
        }

        public override void PostWalk(FunctionDefinition node) {
            EndScope(false);
            Debug.Assert(_head != null);
            base.PostWalk(node);
        }

        public override void PostWalk(DictionaryComprehension node) {
            EndScope(false);
            Debug.Assert(_head != null);
            base.PostWalk(node);
        }

        public override void PostWalk(ListComprehension node) {
            EndScope(false);
            Debug.Assert(_head != null);
            base.PostWalk(node);
        }

        public override void PostWalk(GeneratorExpression node) {
            EndScope(false);
            Debug.Assert(_head != null);
            base.PostWalk(node);
        }


        //public override bool Walk(AwaitExpression node) {
        //    AddSpan(Tuple.Create("", new Span(node.StartIndex, 5)), PredefinedClassificationTypeNames.Keyword);
        //    return base.Walk(node);
        //}

        //public override bool Walk(ForStatement node) {
        //    if (node.IsAsync) {
        //        AddSpan(Tuple.Create("", new Span(node.StartIndex, 5)), PredefinedClassificationTypeNames.Keyword);
        //    }
        //    return base.Walk(node);
        //}

        //public override bool Walk(WithStatement node) {
        //    if (node.IsAsync) {
        //        AddSpan(Tuple.Create("", new Span(node.StartIndex, 5)), PredefinedClassificationTypeNames.Keyword);
        //    }
        //    return base.Walk(node);
        //}
    }
}
