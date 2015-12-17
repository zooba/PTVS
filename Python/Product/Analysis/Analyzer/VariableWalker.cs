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
        private readonly BuiltinsModule _builtins;
        private readonly AnalysisState _state;
        private readonly Dictionary<string, Variable> _vars;
        private readonly List<AnalysisRule> _rules;

        private readonly Stack<int> _nestedIds;
        private readonly Dictionary<Node, int> _knownNestedIds;

        private bool _addRules;

        public VariableWalker(
            PythonLanguageService analyzer,
            AnalysisState state,
            IReadOnlyDictionary<string, Variable> currentVariables,
            IEnumerable<AnalysisRule> currentRules
        ) {
            _analyzer = analyzer;
            _state = state;
            _builtins = _analyzer.BuiltinsModule;
            _vars = new Dictionary<string, Variable>();
            _rules = new List<AnalysisRule>();

            _nestedIds = new Stack<int>();
            _nestedIds.Push(0);
            _knownNestedIds = new Dictionary<Node, int>();

            if (currentVariables != null) {
                foreach (var kv in currentVariables) {
                    _vars[kv.Key] = kv.Value;
                }
            }
            if (currentRules != null) {
                _rules.AddRange(currentRules);
            }
        }

        protected override void OnEnterScope() {
            base.OnEnterScope();
            _nestedIds.Push(0);
        }

        protected override void OnLeaveScope() {
            base.OnLeaveScope();
            _nestedIds.Pop();
        }

        private int GetNestingId(Node node) {
            int next;
            if (!_knownNestedIds.TryGetValue(node, out next)) {
                next = _nestedIds.Pop() + 1;
                _nestedIds.Push(next);
                _knownNestedIds[node] = next;
            }
            return next;
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

        private void Add(VariableKey key, AnalysisValue value) {
            Variable v;
            if (!_vars.TryGetValue(key.Key, out v)) {
                _vars[key.Key] = v = new Variable(_state, key.Key);
            }

            if (value != null) {
                v.AddType(value);
            }
        }

        private T Add<T>(NameExpression name, Func<VariableKey, T> createValue) where T : AnalysisValue {
            return Add((name.Prefix ?? "") + name.Name, createValue);
        }

        private string Add(NameExpression name, AnalysisValue value) {
            return Add((name.Prefix ?? "") + name.Name, value);
        }

        private T Add<T>(string key, Func<VariableKey, T> createValue) where T : AnalysisValue {
            var vk = new VariableKey(_state, CurrentScopeWithSuffix + key);

            var value = createValue(vk);
            Add(vk, value);

            return value;
        }

        private string Add(string key, AnalysisValue value) {
            var vk = new VariableKey(_state, CurrentScopeWithSuffix + key);

            Add(vk, value);

            return vk.Key;
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
                return _builtins.None;
            } else if (expr.Value == (object)true || expr.Value == (object)false) {
                return _builtins.Bool.Instance;
            } else if (expr.Value is int) {
                return _builtins.Int.Instance;
            } else if (expr.Value is BigInteger) {
                // builtins handles the 3 vs 2 difference
                return _builtins.Long.Instance;
            } else if (expr.Value is double) {
                return _builtins.Float.Instance;
            } else if (expr.Value is ByteString) {
                return _builtins.Bytes.Instance;
            } else if (expr.Value is string) {
                return _builtins.Unicode.Instance;
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
            if (variable != null) {
                _rules.Add(new Rules.NameLookup(_state, variable.Key, targets));
                return true;
            }

            var builtin = _builtins.GetAttribute(expr.Name, true);
            if (builtin != null) {
                foreach (var t in targets) {
                    if (!_vars.TryGetValue(t, out variable)) {
                        _vars[t] = variable = new Variable(_state, t);
                    }
                    variable.AddTypes(builtin);
                }
            }

            return false;
        }

        private bool GetAttributeValue(MemberExpression expr, IEnumerable<string> targets) {
            if (expr == null) {
                return false;
            }

            // TODO: Implement attribute handling
            return false;
        }

        private bool GetBinOpValue(BinaryExpression expr, IEnumerable<string> targets) {
            if (expr == null) {
                return false;
            }

            var op = expr.Operator;
            switch(op) {
                case PythonOperator.Divide:
                    if (_state.Features.HasTrueDivision) {
                        op = PythonOperator.TrueDivide;
                    }
                    break;
            }
            var callName = OperatorModule.GetMemberNameForOperator(op);
            if (string.IsNullOrEmpty(callName)) {
                return false;
            }

            var callKey = string.Format("()@{0}", GetNestingId(expr));
            Add(callKey, null);
            Add(new Rules.ImportFromModule(_state, _analyzer.ResolveImport("operator", ""), callName, callKey));

            Assign(string.Format("{0}#$0", callKey), expr.Left);
            Assign(string.Format("{0}#$1", callKey), expr.Right);

            Assign(string.Format("{0}#$r", callKey), null);
            Add(new Rules.ReturnValueLookup(_state, new CallSiteKey(_state, callKey), targets));
            return true;
        }

        private bool GetReturnValue(CallExpression expr, IEnumerable<string> targets) {
            if (expr == null) {
                return false;
            }

            var callKey = string.Format("()@{0}", GetNestingId(expr));
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
            Add(new Rules.ReturnValueLookup(_state, new CallSiteKey(_state, callKey), targets));
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
                GetStringValue(value as StringExpression);

            var targetNames = targets.Select(t => Add(t, type)).ToList();

            var addedRules = !_addRules ||
                GetVariableValue(value as NameExpression, targetNames) ||
                GetAttributeValue(value as MemberExpression, targetNames) ||
                GetBinOpValue(value as BinaryExpression, targetNames) ||
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
            Add(node.Name, key => new ClassValue(key, node));

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
            var key = string.Format("{0}@{1}", node.Name, GetNestingId(node));
            var fi = Add(key, k => new FunctionValue(k, node, CurrentScopeWithSuffix + node.Name));

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

                EnterScope(fi.Key.Key, "#");
                int parameterNumber = 0;
                var defaultsEnum = defaults.GetEnumerator();
                foreach (var p in (node.Parameters?.Parameters).MaybeEnumerate()) {
                    // Add the local variable for the parameter
                    var pv = Add(p.Name, v => {
                        if (p.Kind != ParameterKind.KeywordOnly) {
                            return new ParameterValue(fi.Key, p.Kind, parameterNumber++);
                        }
                        return null;
                    });

                    if (pv != null) {
                        // Add the positional field for the parameter
                        Add(pv.Key, null);
                    }
                    if (defaultsEnum.MoveNext()) {
                        Add(p.Name, defaultsEnum.Current);
                    }
                }

                node.Body?.Walk(this);
                LeaveScope(fi.Key.Key, "#");

                // TODO: Generate decorator calls
                Add(new Rules.NameLookup(_state, fi.Key, Add(node.Name, null)));
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
                Add(new Rules.ImportFromModule(_state, mi, ModuleValue.VariableName, asName));
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
                Add(new Rules.ImportFromModule(_state, mi, importName, asName));
            }


            return false;
        }
    }
}
