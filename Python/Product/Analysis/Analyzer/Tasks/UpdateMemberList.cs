using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    sealed class UpdateMemberList : QueueItem {
        private readonly PythonAst _tree;

        public UpdateMemberList(AnalysisState item, PythonAst tree)
            : base(item) {
            _tree = tree;
        }

        public override async Task PerformAsync(PythonLanguageService analyzer, CancellationToken cancellationToken) {
            var walker = new MemberNameWalker();
            _tree.Walk(walker);
            _item.MemberList.SetValue(walker.Members);
        }

        private class MemberNameWalker : PythonWalkerNonRecursive {
            public readonly Dictionary<string, PythonMemberType> Members;
            private readonly Stack<string> _nameStack;

            public MemberNameWalker() {
                Members = new Dictionary<string, PythonMemberType>();
                _nameStack = new Stack<string>();
            }

            private PythonMemberType Merge(PythonMemberType x, PythonMemberType y) {
                if (x == y) {
                    return x;
                }

                if (x == PythonMemberType.Function || y == PythonMemberType.Function) {
                    return PythonMemberType.Function;
                }

                if (x == PythonMemberType.Class || y == PythonMemberType.Class) {
                    return PythonMemberType.Class;
                }

                if (x == PythonMemberType.Unknown) {
                    return y;
                }
                if (y == PythonMemberType.Unknown) {
                    return x;
                }

                return PythonMemberType.Unknown;
            }

            private void AddMember(string name, PythonMemberType type) {
                var fullName = new StringBuilder();
                foreach (var n in _nameStack) {
                    fullName.Append(n);
                    fullName.Append('.');
                }
                fullName.Append(name);
                var fn = fullName.ToString();

                PythonMemberType existing;
                if (Members.TryGetValue(fn, out existing)) {
                    type = Merge(type, existing);
                }
                Members[fullName.ToString()] = type;
            }

            private void RemoveMember(string name) {
                var fullName = new StringBuilder();
                foreach (var n in _nameStack) {
                    fullName.Append(n);
                    fullName.Append('.');
                }
                fullName.Append(name);
                var fn = fullName.ToString();

                Members.Remove(fn);
            }

            public override bool Walk(ClassDefinition node) {
                AddMember(node.Name, PythonMemberType.Class);
                _nameStack.Push(node.Name);
                return true;
            }

            public override void PostWalk(ClassDefinition node) {
                Debug.Assert(_nameStack.Peek() == node.Name);
                _nameStack.Pop();
            }

            public override bool Walk(FunctionDefinition node) {
                AddMember(node.Name, PythonMemberType.Function);
                return false;
            }

            public override bool Walk(AssignmentStatement node) {
                foreach (var n in node.Left.OfType<NameExpression>()) {
                    AddMember(n.Name, PythonMemberType.Field);
                }
                return false;
            }

            public override bool Walk(ImportStatement node) {
                for (int i = 0; i < node.Names.Count; ++i) {
                    if (node.AsNames != null && node.AsNames[i] != null) {
                        AddMember(node.AsNames[i].Name, PythonMemberType.Module);
                    } else {
                        if (node.Names[i].Names != null && node.Names[i].Names.Count > 0) {
                            AddMember(node.Names[i].Names[0].Name, PythonMemberType.Module);
                        }
                    }
                }
                return false;
            }

            public override bool Walk(DelStatement node) {
                foreach (var n in node.Expressions.OfType<NameExpression>()) {
                    RemoveMember(n.Name);
                }
                return false;
            }

            public override bool Walk(PythonAst node) {
                return true;
            }

            public override bool Walk(SuiteStatement node) {
                return true;
            }

            public override bool Walk(ForStatement node) {
                return true;
            }

            public override bool Walk(IfStatement node) {
                return true;
            }

            public override bool Walk(IfStatementTest node) {
                return true;
            }

            public override bool Walk(WhileStatement node) {
                return true;
            }

            public override bool Walk(WithStatement node) {
                return true;
            }
        }
    }
}
