﻿// Python Tools for Visual Studio
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
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Infrastructure;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    class VariableWalker : ScopeTrackingWalker {
        private readonly PythonLanguageService _analyzer;
        private readonly AnalysisState _state;
        private readonly Dictionary<string, Variable> _vars;
        private readonly List<AnalysisRule> _rules;

        private readonly Stack<List<Node>> _deferredNodes;

        private bool _addRules;

        public VariableWalker(
            PythonLanguageService analyzer,
            AnalysisState state,
            IReadOnlyDictionary<string, Variable> currentVariables,
            IEnumerable<AnalysisRule> currentRules
        ) {
            _analyzer = analyzer;
            _state = state;
            _vars = new Dictionary<string, Variable>();
            _rules = new List<AnalysisRule>();

            _deferredNodes = new Stack<List<Node>>();

            if (currentVariables != null) {
                foreach (var kv in currentVariables) {
                    _vars[kv.Key] = kv.Value;
                }
            }
            if (currentRules != null) {
                _rules.AddRange(currentRules);
            }
        }

        public IReadOnlyDictionary<string, Variable> WalkVariables(PythonAst ast) {
            ast.Walk(this);
            return _vars;
        }

        public IReadOnlyCollection<AnalysisRule> WalkRules(PythonAst ast) {
            _addRules = true;
            try {
                ast.Walk(this);
            } finally {
                _addRules = false;
            }
            return _rules;
        }


        private IEnumerable<string> GetFullNames(NameExpression name) {
            foreach (var scope in CurrentScopesWithSuffix) {
                yield return scope + (name.Prefix ?? "") + name.Name;
            }
        }

        private string Add(NameExpression name, AnalysisValue value) {
            return Add((name.Prefix ?? "") + name.Name, value);
        }

        private string Add(string key, AnalysisValue value) {
            key = CurrentScopeWithSuffix + key;

            Variable v;
            if (!_vars.TryGetValue(key, out v)) {
                _vars[key] = v = new Variable(_state, key);
            }

            if (value != null) {
                v.AddType(value);
            }

            return key;
        }

        private void Add(AnalysisRule rule) {
            if (_addRules) {
                _rules.Add(rule);
            }
        }


        private AnalysisValue GetLiteralValue(ConstantExpression expr) {
            if (expr == null) {
                return null;
            }

            if (expr.Value == null) {
                return BuiltinTypes.None;
            } else if (expr.Value == (object)true || expr.Value == (object)false) {
                return BuiltinTypes.Bool.Instance;
            } else if (expr.Value is int) {
                return BuiltinTypes.Int.Instance;
            } else if (expr.Value is BigInteger) {
                return _analyzer.Configuration.Version.Is3x() ?
                    BuiltinTypes.Int.Instance :
                    BuiltinTypes.Long.Instance;
            } else if (expr.Value is double) {
                return BuiltinTypes.Float.Instance;
            } else if (expr.Value is ByteString) {
                return BuiltinTypes.Bytes.Instance;
            } else if (expr.Value is string) {
                return BuiltinTypes.String.Instance;
            }
            return null;
        }

        private AnalysisValue GetStringValue(StringExpression expr) {
            if ((expr?.Parts?.Count ?? 0) == 0) {
                return null;
            }
            return GetLiteralValue(expr.Parts[0] as ConstantExpression);
        }

        private bool GetVariableValue(NameExpression expr, IEnumerable<string> targets) {
            if (expr == null) {
                return false;
            }

            Variable variable = null;
            foreach (var name in GetFullNames(expr)) {
                if (_vars.TryGetValue(name, out variable)) {
                    break;
                }
            }
            if (variable == null) {
                return false;
            }

            _rules.Add(new Rules.NameLookup(variable.Key, targets));
            
            return true;
        }

        private bool GetAttributeValue(MemberExpression expr, IEnumerable<string> targets) {
            if (expr == null) {
                return false;
            }

            // TODO: Implement attribute handling
            return false;
        }

        private bool GetReturnValue(CallExpression expr, IEnumerable<string> targets) {
            if (expr == null) {
                return false;
            }

            // TODO: Implement call handling
            var callKey = string.Format("()@{0}", expr.Span.Start.Index);
            Assign(callKey, expr.Expression);
            if (expr.Args != null) {
                int argIndex = 0;
                foreach (var a in expr.Args) {
                    var starArg = a.Expression as StarredExpression;
                    if (starArg?.IsStar ?? false) {
                        Assign(callKey + "#*", starArg.Expression);
                    } else if (starArg?.IsDoubleStar ?? false) {
                        Assign(callKey + "#**", starArg.Expression);
                    } else if (a.NameExpression != null) {
                        Assign(string.Format("{0}#${1}", callKey, a.Name), a.Expression);
                    } else {
                        Assign(string.Format("{0}#${1}", callKey, argIndex++), a.Expression);
                    }
                }
            }
            Assign(string.Format("{0}#$r", callKey), null);
            Add(new Rules.ReturnValueLookup(new VariableKey(_state, callKey), targets));
            return true;
        }

        private string GetTargetName(NameExpression expr) {
            if (expr == null) {
                return null;
            }
            return (expr.Prefix ?? "") + expr.Name;
        }

        private string GetTargetName(MemberExpression expr) {
            if (expr == null) {
                return null;
            }
            var baseName = GetTargetName(expr.Expression);
            if (baseName == null) {
                return null;
            }
            return baseName + "." + expr.Name;
        }

        private string GetTargetName(Expression expr) {
            return
                GetTargetName(expr as NameExpression) ??
                GetTargetName(expr as MemberExpression);
        }

        private void Assign(IEnumerable<Expression> targets, Expression value) {
            var targetNames = targets
                .MaybeEnumerate()
                .Select(t => GetTargetName(t))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            Assign(targetNames, value);
        }

        private void Assign(string target, Expression value) {
            Assign(Enumerable.Repeat(target, 1), value);
        }

        private void Assign(IEnumerable<string> targets, Expression value) {
            AnalysisValue type =
                GetLiteralValue(value as ConstantExpression) ??
                GetStringValue(value as StringExpression) ??
                AnalysisValue.Empty;

            var targetNames = targets.Select(t => Add(t, type)).ToList();

            var addedRules = !_addRules ||
                GetVariableValue(value as NameExpression, targetNames) ||
                GetAttributeValue(value as MemberExpression, targetNames) ||
                GetReturnValue(value as CallExpression, targetNames);
        }

        public override bool Walk(AssignmentStatement node) {
            if (node.Targets != null) {
                node.Expression.Walk(this);

                Assign(node.Targets, node.Expression);
            }

            return false;
        }

        public override bool Walk(CallExpression node) {
            return false;
        }

        public override bool Walk(ClassDefinition node) {
            Add(node.Name, new ClassInfo(node));

            if (Defer(node)) {
                foreach (var d in node.Decorators.MaybeEnumerate()) {
                    d.Walk(this);
                }
            } else {
                EnterScope(node.Name, ".");
                node.Body?.Walk(this);
                LeaveScope(node.Name, ".");
            }

            return false;
        }

        public override bool Walk(FunctionDefinition node) {
            var fullName = Add(node.Name, null);
            var fi = new FunctionInfo(node, fullName);
            Add(node.Name, fi);

            if (Defer(node)) {
                foreach (var d in node.Decorators.MaybeEnumerate()) {
                    d.Walk(this);
                }
            } else {
                var defaults = new List<AnalysisValue>();
                foreach (var p in (node.Parameters?.Parameters).MaybeEnumerate()) {
                    // TODO: Walk annotation

                    // TODO: Walk default value
                    defaults.Add(null);
                }

                EnterScope(fi.Key, "#");
                int parameterNumber = 0;
                var defaultsEnum = defaults.GetEnumerator();
                foreach (var p in (node.Parameters?.Parameters).MaybeEnumerate()) {
                    ParameterInfo pi;
                    string pKey;
                    if (p.Kind == ParameterKind.List) {
                        pi = ParameterInfo.ListParameter;
                    } else if (p.Kind == ParameterKind.Dictionary) {
                        pi = ParameterInfo.DictParameter;
                    } else if (p.Kind == ParameterKind.KeywordOnly) {
                        pi = null;
                    } else {
                        pi = ParameterInfo.Create(parameterNumber++);
                    }
                    pKey = Add(p.Name, pi);
                    if (pi != null) {
                        Add(pi.KeySuffix, null);
                    }
                    if (defaultsEnum.MoveNext()) {
                        Add(p.Name, defaultsEnum.Current);
                    }
                }

                node.Body?.Walk(this);
                LeaveScope(fi.Key, "#");
            }

            return false;
        }

        public override bool Walk(ReturnStatement node) {
            if (!node.IsExpressionEmpty) {
                Assign("$r", node.Expression);
            }
            return false;
        }

        public override bool Walk(ImportStatement node) {
            if (node.Names == null) {
                return false;
            }

            foreach (var n in node.Names) {
                var importName = ImportStatement.GetImportName(n);
                var asName = ImportStatement.GetAsName(n);
                var mi = _analyzer.ResolveImport(importName, "");
                Add(asName, null);
                Add(new Rules.ImportFromModule(mi, ModuleInfo.VariableName, asName));
            }

            return false;
        }

        public override bool Walk(FromImportStatement node) {
            if (node.Names == null) {
                return false;
            }

            var import = node.Root.MakeString();
            var mi = _analyzer.ResolveImport(import, "");

            var importNames = new List<string>();
            var asNames = new List<string>();
            foreach(var n in node.Names) {
                var importName = FromImportStatement.GetImportName(n);
                var asName = FromImportStatement.GetAsName(n);
                if (importName == null || asName == null) {
                    continue;
                }

                if (asName != "*") {
                    Add(asName, null);
                }
                Add(new Rules.ImportFromModule(mi, importName, asName));
            }


            return false;
        }
    }
}
