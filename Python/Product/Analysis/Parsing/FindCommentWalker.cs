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

using Microsoft.PythonTools.Analysis.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Parsing {
    public sealed class FindCommentWalker : PythonWalker {
        private readonly Tokenization _tokenization;
        private readonly Node _target;
        private string _comment;

        private enum Stage { SearchForNode, SearchForComment, Finish };
        private Stage _stage;

        public static string Find(PythonAst tree, Node target) {
            var walker = new FindCommentWalker(tree.Tokenization, target);
            tree.Walk(walker);
            return walker.Comment;
        }

        private FindCommentWalker(Tokenization tokenization, Node target) {
            _tokenization = tokenization;
            _target = target;
            _stage = Stage.SearchForNode;
        }

        public string Comment => _comment;

        private bool OnWalk(Node node) {
            if (_stage == Stage.SearchForNode && node == _target) {
                _stage = Stage.SearchForComment;
            } else if (_target.Span.End < node.Span.Start) {
                _stage = Stage.Finish;
            }
            return _stage != Stage.Finish;
        }

        private void OnPostWalk(Node node) {
            if (_stage != Stage.SearchForComment) {
                return;
            }
            if (node.AfterNode.Start.Line > _target.Span.End.Line) {
                _stage = Stage.Finish;
            } else if (node.AfterNode.Length > 1) {
                var text = _tokenization.GetTokenText(node.AfterNode)?.Trim() ?? "";
                if (text.StartsWith("#")) {
                    _comment = text;
                    _stage = Stage.Finish;
                }
            }
        }

        public override bool Walk(Arg node) => OnWalk(node);
        public override bool Walk(AsExpression node) => OnWalk(node);
        public override bool Walk(AssertStatement node) => OnWalk(node);
        public override bool Walk(AssignmentStatement node) => OnWalk(node);
        public override bool Walk(AugmentedAssignStatement node) => OnWalk(node);
        public override bool Walk(AwaitExpression node) => OnWalk(node);
        public override bool Walk(BackQuoteExpression node) => OnWalk(node);
        public override bool Walk(BinaryExpression node) => OnWalk(node);
        public override bool Walk(BreakStatement node) => OnWalk(node);
        public override bool Walk(CallExpression node) => OnWalk(node);
        public override bool Walk(ClassDefinition node) => OnWalk(node);
        public override bool Walk(ComprehensionFor node) => OnWalk(node);
        public override bool Walk(ComprehensionIf node) => OnWalk(node);
        public override bool Walk(ConditionalExpression node) => OnWalk(node);
        public override bool Walk(ConstantExpression node) => OnWalk(node);
        public override bool Walk(ContinueStatement node) => OnWalk(node);
        public override bool Walk(DecoratorStatement node) => OnWalk(node);
        public override bool Walk(DelStatement node) => OnWalk(node);
        public override bool Walk(DictionaryComprehension node) => OnWalk(node);
        public override bool Walk(DictionaryExpression node) => OnWalk(node);
        public override bool Walk(DottedName node) => OnWalk(node);
        public override bool Walk(EmptyStatement node) => OnWalk(node);
        public override bool Walk(ErrorExpression node) => OnWalk(node);
        public override bool Walk(ErrorStatement node) => OnWalk(node);
        public override bool Walk(ExecStatement node) => OnWalk(node);
        public override bool Walk(ExpressionStatement node) => OnWalk(node);
        public override bool Walk(ForStatement node) => OnWalk(node);
        public override bool Walk(FromImportStatement node) => OnWalk(node);
        public override bool Walk(FunctionDefinition node) => OnWalk(node);
        public override bool Walk(GeneratorExpression node) => OnWalk(node);
        public override bool Walk(GlobalStatement node) => OnWalk(node);
        public override bool Walk(IfStatement node) => OnWalk(node);
        public override bool Walk(ImportStatement node) => OnWalk(node);
        public override bool Walk(IndexExpression node) => OnWalk(node);
        public override bool Walk(LambdaExpression node) => OnWalk(node);
        public override bool Walk(ListComprehension node) => OnWalk(node);
        public override bool Walk(ListExpression node) => OnWalk(node);
        public override bool Walk(MemberExpression node) => OnWalk(node);
        public override bool Walk(NameExpression node) => OnWalk(node);
        public override bool Walk(NonlocalStatement node) => OnWalk(node);
        public override bool Walk(Parameter node) => OnWalk(node);
        public override bool Walk(ParameterList node) => OnWalk(node);
        public override bool Walk(ParenthesisExpression node) => OnWalk(node);
        public override bool Walk(PassStatement node) => OnWalk(node);
        public override bool Walk(PrintStatement node) => OnWalk(node);
        public override bool Walk(PythonAst node) => OnWalk(node);
        public override bool Walk(RaiseStatement node) => OnWalk(node);
        public override bool Walk(ReturnStatement node) => OnWalk(node);
        public override bool Walk(SetComprehension node) => OnWalk(node);
        public override bool Walk(SetExpression node) => OnWalk(node);
        public override bool Walk(SliceExpression node) => OnWalk(node);
        public override bool Walk(StarredExpression node) => OnWalk(node);
        public override bool Walk(StringExpression node) => OnWalk(node);
        public override bool Walk(SuiteStatement node) => OnWalk(node);
        public override bool Walk(TryStatement node) => OnWalk(node);
        public override bool Walk(TupleExpression node) => OnWalk(node);
        public override bool Walk(UnaryExpression node) => OnWalk(node);
        public override bool Walk(WhileStatement node) => OnWalk(node);
        public override bool Walk(WithStatement node) => OnWalk(node);
        public override bool Walk(YieldExpression node) => OnWalk(node);
        public override bool Walk(YieldFromExpression node) => OnWalk(node);

        public override void PostWalk(Arg node) => OnPostWalk(node);
        public override void PostWalk(AsExpression node) => OnPostWalk(node);
        public override void PostWalk(AssertStatement node) => OnPostWalk(node);
        public override void PostWalk(AssignmentStatement node) => OnPostWalk(node);
        public override void PostWalk(AugmentedAssignStatement node) => OnPostWalk(node);
        public override void PostWalk(AwaitExpression node) => OnPostWalk(node);
        public override void PostWalk(BackQuoteExpression node) => OnPostWalk(node);
        public override void PostWalk(BinaryExpression node) => OnPostWalk(node);
        public override void PostWalk(BreakStatement node) => OnPostWalk(node);
        public override void PostWalk(CallExpression node) => OnPostWalk(node);
        public override void PostWalk(ClassDefinition node) => OnPostWalk(node);
        public override void PostWalk(ComprehensionFor node) => OnPostWalk(node);
        public override void PostWalk(ComprehensionIf node) => OnPostWalk(node);
        public override void PostWalk(ConditionalExpression node) => OnPostWalk(node);
        public override void PostWalk(ConstantExpression node) => OnPostWalk(node);
        public override void PostWalk(ContinueStatement node) => OnPostWalk(node);
        public override void PostWalk(DecoratorStatement node) => OnPostWalk(node);
        public override void PostWalk(DelStatement node) => OnPostWalk(node);
        public override void PostWalk(DictionaryComprehension node) => OnPostWalk(node);
        public override void PostWalk(DictionaryExpression node) => OnPostWalk(node);
        public override void PostWalk(DottedName node) => OnPostWalk(node);
        public override void PostWalk(EmptyStatement node) => OnPostWalk(node);
        public override void PostWalk(ErrorExpression node) => OnPostWalk(node);
        public override void PostWalk(ErrorStatement node) => OnPostWalk(node);
        public override void PostWalk(ExecStatement node) => OnPostWalk(node);
        public override void PostWalk(ExpressionStatement node) => OnPostWalk(node);
        public override void PostWalk(ForStatement node) => OnPostWalk(node);
        public override void PostWalk(FromImportStatement node) => OnPostWalk(node);
        public override void PostWalk(FunctionDefinition node) => OnPostWalk(node);
        public override void PostWalk(GeneratorExpression node) => OnPostWalk(node);
        public override void PostWalk(GlobalStatement node) => OnPostWalk(node);
        public override void PostWalk(IfStatement node) => OnPostWalk(node);
        public override void PostWalk(ImportStatement node) => OnPostWalk(node);
        public override void PostWalk(IndexExpression node) => OnPostWalk(node);
        public override void PostWalk(LambdaExpression node) => OnPostWalk(node);
        public override void PostWalk(ListComprehension node) => OnPostWalk(node);
        public override void PostWalk(ListExpression node) => OnPostWalk(node);
        public override void PostWalk(MemberExpression node) => OnPostWalk(node);
        public override void PostWalk(NameExpression node) => OnPostWalk(node);
        public override void PostWalk(NonlocalStatement node) => OnPostWalk(node);
        public override void PostWalk(Parameter node) => OnPostWalk(node);
        public override void PostWalk(ParameterList node) => OnPostWalk(node);
        public override void PostWalk(ParenthesisExpression node) => OnPostWalk(node);
        public override void PostWalk(PassStatement node) => OnPostWalk(node);
        public override void PostWalk(PrintStatement node) => OnPostWalk(node);
        public override void PostWalk(PythonAst node) => OnPostWalk(node);
        public override void PostWalk(RaiseStatement node) => OnPostWalk(node);
        public override void PostWalk(ReturnStatement node) => OnPostWalk(node);
        public override void PostWalk(SetComprehension node) => OnPostWalk(node);
        public override void PostWalk(SetExpression node) => OnPostWalk(node);
        public override void PostWalk(SliceExpression node) => OnPostWalk(node);
        public override void PostWalk(StarredExpression node) => OnPostWalk(node);
        public override void PostWalk(StringExpression node) => OnPostWalk(node);
        public override void PostWalk(SuiteStatement node) => OnPostWalk(node);
        public override void PostWalk(TryStatement node) => OnPostWalk(node);
        public override void PostWalk(TupleExpression node) => OnPostWalk(node);
        public override void PostWalk(UnaryExpression node) => OnPostWalk(node);
        public override void PostWalk(WhileStatement node) => OnPostWalk(node);
        public override void PostWalk(WithStatement node) => OnPostWalk(node);
        public override void PostWalk(YieldExpression node) => OnPostWalk(node);
        public override void PostWalk(YieldFromExpression node) => OnPostWalk(node);
    }
}
