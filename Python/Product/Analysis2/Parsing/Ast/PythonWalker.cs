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


namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    /// <summary>
    /// PythonWalker class - The Python AST Walker (default result is true)
    /// </summary>
    public class PythonWalker {

        // AwaitExpression
        public virtual bool Walk(AwaitExpression node) { return true; }
        public virtual void PostWalk(AwaitExpression node) { }

        // BackQuoteExpression
        public virtual bool Walk(BackQuoteExpression node) { return true; }
        public virtual void PostWalk(BackQuoteExpression node) { }

        // BinaryExpression
        public virtual bool Walk(BinaryExpression node) { return true; }
        public virtual void PostWalk(BinaryExpression node) { }

        // CallExpression
        public virtual bool Walk(CallExpression node) { return true; }
        public virtual void PostWalk(CallExpression node) { }

        // CommentExpression
        public virtual bool Walk(CommentExpression node) { return true; }
        public virtual void PostWalk(CommentExpression node) { }

        // ConditionalExpression
        public virtual bool Walk(ConditionalExpression node) { return true; }
        public virtual void PostWalk(ConditionalExpression node) { }

        // ConstantExpression
        public virtual bool Walk(ConstantExpression node) { return true; }
        public virtual void PostWalk(ConstantExpression node) { }

        // DictionaryComprehension
        public virtual bool Walk(DictionaryComprehension node) { return true; }
        public virtual void PostWalk(DictionaryComprehension node) { }

        // DictionaryExpression
        public virtual bool Walk(DictionaryExpression node) { return true; }
        public virtual void PostWalk(DictionaryExpression node) { }

        // ErrorExpression
        public virtual bool Walk(ErrorExpression node) { return true; }
        public virtual void PostWalk(ErrorExpression node) { }

        // GeneratorExpression
        public virtual bool Walk(GeneratorExpression node) { return true; }
        public virtual void PostWalk(GeneratorExpression node) { }

        // IndexExpression
        public virtual bool Walk(IndexExpression node) { return true; }
        public virtual void PostWalk(IndexExpression node) { }

        // LambdaExpression
        public virtual bool Walk(LambdaExpression node) { return true; }
        public virtual void PostWalk(LambdaExpression node) { }

        // ListComprehension
        public virtual bool Walk(ListComprehension node) { return true; }
        public virtual void PostWalk(ListComprehension node) { }

        // ListExpression
        public virtual bool Walk(ListExpression node) { return true; }
        public virtual void PostWalk(ListExpression node) { }

        // MemberExpression
        public virtual bool Walk(MemberExpression node) { return true; }
        public virtual void PostWalk(MemberExpression node) { }

        // NameExpression
        public virtual bool Walk(NameExpression node) { return true; }
        public virtual void PostWalk(NameExpression node) { }

        // ParenthesisExpression
        public virtual bool Walk(ParenthesisExpression node) { return true; }
        public virtual void PostWalk(ParenthesisExpression node) { }

        // SetComprehension
        public virtual bool Walk(SetComprehension node) { return true; }
        public virtual void PostWalk(SetComprehension node) { }

        // SetExpression
        public virtual bool Walk(SetExpression node) { return true; }
        public virtual void PostWalk(SetExpression node) { }

        // SliceExpression
        public virtual bool Walk(SliceExpression node) { return true; }
        public virtual void PostWalk(SliceExpression node) { }

        // TupleExpression
        public virtual bool Walk(TupleExpression node) { return true; }
        public virtual void PostWalk(TupleExpression node) { }

        // UnaryExpression
        public virtual bool Walk(UnaryExpression node) { return true; }
        public virtual void PostWalk(UnaryExpression node) { }

        // YieldExpression
        public virtual bool Walk(YieldExpression node) { return true; }
        public virtual void PostWalk(YieldExpression node) { }

        // YieldFromExpression
        public virtual bool Walk(YieldFromExpression node) { return true; }
        public virtual void PostWalk(YieldFromExpression node) { }

        // StarredExpression
        public virtual bool Walk(StarredExpression node) { return true; }
        public virtual void PostWalk(StarredExpression node) { }

        // AssertStatement
        public virtual bool Walk(AssertStatement node) { return true; }
        public virtual void PostWalk(AssertStatement node) { }

        // AssignmentStatement
        public virtual bool Walk(AssignmentStatement node) { return true; }
        public virtual void PostWalk(AssignmentStatement node) { }

        // AugmentedAssignStatement
        public virtual bool Walk(AugmentedAssignStatement node) { return true; }
        public virtual void PostWalk(AugmentedAssignStatement node) { }

        // BreakStatement
        public virtual bool Walk(BreakStatement node) { return true; }
        public virtual void PostWalk(BreakStatement node) { }

        // ClassDefinition
        public virtual bool Walk(ClassDefinition node) { return true; }
        public virtual void PostWalk(ClassDefinition node) { }

        // ContinueStatement
        public virtual bool Walk(ContinueStatement node) { return true; }
        public virtual void PostWalk(ContinueStatement node) { }

        // DelStatement
        public virtual bool Walk(DelStatement node) { return true; }
        public virtual void PostWalk(DelStatement node) { }

        // EmptyStatement
        public virtual bool Walk(EmptyStatement node) { return true; }
        public virtual void PostWalk(EmptyStatement node) { }

        // ExecStatement
        public virtual bool Walk(ExecStatement node) { return true; }
        public virtual void PostWalk(ExecStatement node) { }

        // ExpressionStatement
        public virtual bool Walk(ExpressionStatement node) { return true; }
        public virtual void PostWalk(ExpressionStatement node) { }

        // ForStatement
        public virtual bool Walk(ForStatement node) { return true; }
        public virtual void PostWalk(ForStatement node) { }

        // FromImportStatement
        public virtual bool Walk(FromImportStatement node) { return true; }
        public virtual void PostWalk(FromImportStatement node) { }

        // FunctionDefinition
        public virtual bool Walk(FunctionDefinition node) { return true; }
        public virtual void PostWalk(FunctionDefinition node) { }

        // GlobalStatement
        public virtual bool Walk(GlobalStatement node) { return true; }
        public virtual void PostWalk(GlobalStatement node) { }

        // NonlocalStatement
        public virtual bool Walk(NonlocalStatement node) { return true; }
        public virtual void PostWalk(NonlocalStatement node) { }

        // IfStatement
        public virtual bool Walk(IfStatement node) { return true; }
        public virtual void PostWalk(IfStatement node) { }

        // ImportStatement
        public virtual bool Walk(ImportStatement node) { return true; }
        public virtual void PostWalk(ImportStatement node) { }

        // PassStatement
        public virtual bool Walk(PassStatement node) { return true; }
        public virtual void PostWalk(PassStatement node) { }

        // PrintStatement
        public virtual bool Walk(PrintStatement node) { return true; }
        public virtual void PostWalk(PrintStatement node) { }

        // PythonAst
        public virtual bool Walk(PythonAst node) { return true; }
        public virtual void PostWalk(PythonAst node) { }

        // RaiseStatement
        public virtual bool Walk(RaiseStatement node) { return true; }
        public virtual void PostWalk(RaiseStatement node) { }

        // ReturnStatement
        public virtual bool Walk(ReturnStatement node) { return true; }
        public virtual void PostWalk(ReturnStatement node) { }

        // SuiteStatement
        public virtual bool Walk(SuiteStatement node) { return true; }
        public virtual void PostWalk(SuiteStatement node) { }

        // TryStatement
        public virtual bool Walk(TryStatement node) { return true; }
        public virtual void PostWalk(TryStatement node) { }

        // WhileStatement
        public virtual bool Walk(WhileStatement node) { return true; }
        public virtual void PostWalk(WhileStatement node) { }

        // WithStatement
        public virtual bool Walk(WithStatement node) { return true; }
        public virtual void PostWalk(WithStatement node) { }

        // Arg
        public virtual bool Walk(Arg node) { return true; }
        public virtual void PostWalk(Arg node) { }

        // ComprehensionFor
        public virtual bool Walk(ComprehensionFor node) { return true; }
        public virtual void PostWalk(ComprehensionFor node) { }

        // ComprehensionIf
        public virtual bool Walk(ComprehensionIf node) { return true; }
        public virtual void PostWalk(ComprehensionIf node) { }

        // DottedName
        public virtual bool Walk(DottedName node) { return true; }
        public virtual void PostWalk(DottedName node) { }

        // IfStatementTest
        public virtual bool Walk(IfStatementTest node) { return true; }
        public virtual void PostWalk(IfStatementTest node) { }

        // ModuleName
        public virtual bool Walk(ModuleName node) { return true; }
        public virtual void PostWalk(ModuleName node) { }

        // Parameter
        public virtual bool Walk(Parameter node) { return true; }
        public virtual void PostWalk(Parameter node) { }

        // ParameterList
        public virtual bool Walk(ParameterList node) { return true; }
        public virtual void PostWalk(ParameterList node) { }

        // RelativeModuleName
        public virtual bool Walk(RelativeModuleName node) { return true; }
        public virtual void PostWalk(RelativeModuleName node) { }

        // TryStatementHandler
        public virtual bool Walk(TryStatementHandler node) { return true; }
        public virtual void PostWalk(TryStatementHandler node) { }

        // ErrorStatement
        public virtual bool Walk(ErrorStatement node) { return true; }
        public virtual void PostWalk(ErrorStatement node) { }

        // DecoratorStatement
        public virtual bool Walk(DecoratorStatement node) { return true; }
        public virtual void PostWalk(DecoratorStatement node) { }
    }


    /// <summary>
    /// PythonWalkerNonRecursive class - The Python AST Walker (default result is false)
    /// </summary>
    public class PythonWalkerNonRecursive : PythonWalker {
        // AwaitExpression
        public override bool Walk(AwaitExpression node) { return false; }
        public override void PostWalk(AwaitExpression node) { }

        // BackQuoteExpression
        public override bool Walk(BackQuoteExpression node) { return false; }
        public override void PostWalk(BackQuoteExpression node) { }

        // BinaryExpression
        public override bool Walk(BinaryExpression node) { return false; }
        public override void PostWalk(BinaryExpression node) { }

        // CallExpression
        public override bool Walk(CallExpression node) { return false; }
        public override void PostWalk(CallExpression node) { }

        // CommentExpression
        public override bool Walk(CommentExpression node) { return false; }
        public override void PostWalk(CommentExpression node) { }

        // ConditionalExpression
        public override bool Walk(ConditionalExpression node) { return false; }
        public override void PostWalk(ConditionalExpression node) { }

        // ConstantExpression
        public override bool Walk(ConstantExpression node) { return false; }
        public override void PostWalk(ConstantExpression node) { }

        // DictionaryComprehension
        public override bool Walk(DictionaryComprehension node) { return false; }
        public override void PostWalk(DictionaryComprehension node) { }

        // DictionaryExpression
        public override bool Walk(DictionaryExpression node) { return false; }
        public override void PostWalk(DictionaryExpression node) { }

        // ErrorExpression
        public override bool Walk(ErrorExpression node) { return false; }
        public override void PostWalk(ErrorExpression node) { }

        // GeneratorExpression
        public override bool Walk(GeneratorExpression node) { return false; }
        public override void PostWalk(GeneratorExpression node) { }

        // IndexExpression
        public override bool Walk(IndexExpression node) { return false; }
        public override void PostWalk(IndexExpression node) { }

        // LambdaExpression
        public override bool Walk(LambdaExpression node) { return false; }
        public override void PostWalk(LambdaExpression node) { }

        // ListComprehension
        public override bool Walk(ListComprehension node) { return false; }
        public override void PostWalk(ListComprehension node) { }

        // ListExpression
        public override bool Walk(ListExpression node) { return false; }
        public override void PostWalk(ListExpression node) { }

        // MemberExpression
        public override bool Walk(MemberExpression node) { return false; }
        public override void PostWalk(MemberExpression node) { }

        // NameExpression
        public override bool Walk(NameExpression node) { return false; }
        public override void PostWalk(NameExpression node) { }

        // ParenthesisExpression
        public override bool Walk(ParenthesisExpression node) { return false; }
        public override void PostWalk(ParenthesisExpression node) { }

        // SetComprehension
        public override bool Walk(SetComprehension node) { return false; }
        public override void PostWalk(SetComprehension node) { }

        // SetExpression
        public override bool Walk(SetExpression node) { return false; }
        public override void PostWalk(SetExpression node) { }

        // SliceExpression
        public override bool Walk(SliceExpression node) { return false; }
        public override void PostWalk(SliceExpression node) { }

        // TupleExpression
        public override bool Walk(TupleExpression node) { return false; }
        public override void PostWalk(TupleExpression node) { }

        // UnaryExpression
        public override bool Walk(UnaryExpression node) { return false; }
        public override void PostWalk(UnaryExpression node) { }

        // YieldExpression
        public override bool Walk(YieldExpression node) { return false; }
        public override void PostWalk(YieldExpression node) { }

        // YieldFromExpression
        public override bool Walk(YieldFromExpression node) { return false; }
        public override void PostWalk(YieldFromExpression node) { }

        // StarredExpression
        public override bool Walk(StarredExpression node) { return false; }
        public override void PostWalk(StarredExpression node) { }

        // AssertStatement
        public override bool Walk(AssertStatement node) { return false; }
        public override void PostWalk(AssertStatement node) { }

        // AssignmentStatement
        public override bool Walk(AssignmentStatement node) { return false; }
        public override void PostWalk(AssignmentStatement node) { }

        // AugmentedAssignStatement
        public override bool Walk(AugmentedAssignStatement node) { return false; }
        public override void PostWalk(AugmentedAssignStatement node) { }

        // BreakStatement
        public override bool Walk(BreakStatement node) { return false; }
        public override void PostWalk(BreakStatement node) { }

        // ClassDefinition
        public override bool Walk(ClassDefinition node) { return false; }
        public override void PostWalk(ClassDefinition node) { }

        // ContinueStatement
        public override bool Walk(ContinueStatement node) { return false; }
        public override void PostWalk(ContinueStatement node) { }

        // DelStatement
        public override bool Walk(DelStatement node) { return false; }
        public override void PostWalk(DelStatement node) { }

        // EmptyStatement
        public override bool Walk(EmptyStatement node) { return false; }
        public override void PostWalk(EmptyStatement node) { }

        // ExecStatement
        public override bool Walk(ExecStatement node) { return false; }
        public override void PostWalk(ExecStatement node) { }

        // ExpressionStatement
        public override bool Walk(ExpressionStatement node) { return false; }
        public override void PostWalk(ExpressionStatement node) { }

        // ForStatement
        public override bool Walk(ForStatement node) { return false; }
        public override void PostWalk(ForStatement node) { }

        // FromImportStatement
        public override bool Walk(FromImportStatement node) { return false; }
        public override void PostWalk(FromImportStatement node) { }

        // FunctionDefinition
        public override bool Walk(FunctionDefinition node) { return false; }
        public override void PostWalk(FunctionDefinition node) { }

        // GlobalStatement
        public override bool Walk(GlobalStatement node) { return false; }
        public override void PostWalk(GlobalStatement node) { }

        // NonlocalStatement
        public override bool Walk(NonlocalStatement node) { return false; }
        public override void PostWalk(NonlocalStatement node) { }

        // IfStatement
        public override bool Walk(IfStatement node) { return false; }
        public override void PostWalk(IfStatement node) { }

        // ImportStatement
        public override bool Walk(ImportStatement node) { return false; }
        public override void PostWalk(ImportStatement node) { }

        // PassStatement
        public override bool Walk(PassStatement node) { return false; }
        public override void PostWalk(PassStatement node) { }

        // PrintStatement
        public override bool Walk(PrintStatement node) { return false; }
        public override void PostWalk(PrintStatement node) { }

        // PythonAst
        public override bool Walk(PythonAst node) { return false; }
        public override void PostWalk(PythonAst node) { }

        // RaiseStatement
        public override bool Walk(RaiseStatement node) { return false; }
        public override void PostWalk(RaiseStatement node) { }

        // ReturnStatement
        public override bool Walk(ReturnStatement node) { return false; }
        public override void PostWalk(ReturnStatement node) { }

        // SuiteStatement
        public override bool Walk(SuiteStatement node) { return false; }
        public override void PostWalk(SuiteStatement node) { }

        // TryStatement
        public override bool Walk(TryStatement node) { return false; }
        public override void PostWalk(TryStatement node) { }

        // WhileStatement
        public override bool Walk(WhileStatement node) { return false; }
        public override void PostWalk(WhileStatement node) { }

        // WithStatement
        public override bool Walk(WithStatement node) { return false; }
        public override void PostWalk(WithStatement node) { }

        // Arg
        public override bool Walk(Arg node) { return false; }
        public override void PostWalk(Arg node) { }

        // ComprehensionFor
        public override bool Walk(ComprehensionFor node) { return false; }
        public override void PostWalk(ComprehensionFor node) { }

        // ComprehensionIf
        public override bool Walk(ComprehensionIf node) { return false; }
        public override void PostWalk(ComprehensionIf node) { }

        // DottedName
        public override bool Walk(DottedName node) { return false; }
        public override void PostWalk(DottedName node) { }

        // IfStatementTest
        public override bool Walk(IfStatementTest node) { return false; }
        public override void PostWalk(IfStatementTest node) { }

        // ModuleName
        public override bool Walk(ModuleName node) { return false; }
        public override void PostWalk(ModuleName node) { }

        // Parameter
        public override bool Walk(Parameter node) { return false; }
        public override void PostWalk(Parameter node) { }

        // ParameterList
        public override bool Walk(ParameterList node) { return false; }
        public override void PostWalk(ParameterList node) { }

        // RelativeModuleName
        public override bool Walk(RelativeModuleName node) { return false; }
        public override void PostWalk(RelativeModuleName node) { }

        // TryStatementHandler
        public override bool Walk(TryStatementHandler node) { return false; }
        public override void PostWalk(TryStatementHandler node) { }

        // ErrorStatement
        public override bool Walk(ErrorStatement node) { return false; }
        public override void PostWalk(ErrorStatement node) { }

        // DecoratorStatement
        public override bool Walk(DecoratorStatement node) { return false; }
        public override void PostWalk(DecoratorStatement node) { }
    }

    /// <summary>
    /// PythonWalkerWithLocation class - The Python AST Walker (default result
    /// is true if the node contains Location, otherwise false)
    /// </summary>
    public class PythonWalkerWithLocation : PythonWalker {
        public readonly int Location;

        public PythonWalkerWithLocation(int location) {
            Location = location;
        }

        // AwaitExpression
        public override bool Walk(AwaitExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // BackQuoteExpression
        public override bool Walk(BackQuoteExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // BinaryExpression
        public override bool Walk(BinaryExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // CallExpression
        public override bool Walk(CallExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // CommentExpression
        public override bool Walk(CommentExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ConditionalExpression
        public override bool Walk(ConditionalExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ConstantExpression
        public override bool Walk(ConstantExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // DictionaryComprehension
        public override bool Walk(DictionaryComprehension node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // DictionaryExpression
        public override bool Walk(DictionaryExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ErrorExpression
        public override bool Walk(ErrorExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // GeneratorExpression
        public override bool Walk(GeneratorExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // IndexExpression
        public override bool Walk(IndexExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // LambdaExpression
        public override bool Walk(LambdaExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ListComprehension
        public override bool Walk(ListComprehension node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ListExpression
        public override bool Walk(ListExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // MemberExpression
        public override bool Walk(MemberExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // NameExpression
        public override bool Walk(NameExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ParenthesisExpression
        public override bool Walk(ParenthesisExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // SetComprehension
        public override bool Walk(SetComprehension node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // SetExpression
        public override bool Walk(SetExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // SliceExpression
        public override bool Walk(SliceExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // TupleExpression
        public override bool Walk(TupleExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // UnaryExpression
        public override bool Walk(UnaryExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // YieldExpression
        public override bool Walk(YieldExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // YieldFromExpression
        public override bool Walk(YieldFromExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // StarredExpression
        public override bool Walk(StarredExpression node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // AssertStatement
        public override bool Walk(AssertStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // AssignmentStatement
        public override bool Walk(AssignmentStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // AugmentedAssignStatement
        public override bool Walk(AugmentedAssignStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // BreakStatement
        public override bool Walk(BreakStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ClassDefinition
        public override bool Walk(ClassDefinition node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ContinueStatement
        public override bool Walk(ContinueStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // DelStatement
        public override bool Walk(DelStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // EmptyStatement
        public override bool Walk(EmptyStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ExecStatement
        public override bool Walk(ExecStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ExpressionStatement
        public override bool Walk(ExpressionStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ForStatement
        public override bool Walk(ForStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // FromImportStatement
        public override bool Walk(FromImportStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // FunctionDefinition
        public override bool Walk(FunctionDefinition node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // GlobalStatement
        public override bool Walk(GlobalStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // NonlocalStatement
        public override bool Walk(NonlocalStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // IfStatement
        public override bool Walk(IfStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ImportStatement
        public override bool Walk(ImportStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // PassStatement
        public override bool Walk(PassStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // PrintStatement
        public override bool Walk(PrintStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // PythonAst
        public override bool Walk(PythonAst node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // RaiseStatement
        public override bool Walk(RaiseStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ReturnStatement
        public override bool Walk(ReturnStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // SuiteStatement
        public override bool Walk(SuiteStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // TryStatement
        public override bool Walk(TryStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // WhileStatement
        public override bool Walk(WhileStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // WithStatement
        public override bool Walk(WithStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // Arg
        public override bool Walk(Arg node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ComprehensionFor
        public override bool Walk(ComprehensionFor node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ComprehensionIf
        public override bool Walk(ComprehensionIf node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // DottedName
        public override bool Walk(DottedName node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // IfStatementTest
        public override bool Walk(IfStatementTest node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ModuleName
        public override bool Walk(ModuleName node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // Parameter
        public override bool Walk(Parameter node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // Parameter
        public override bool Walk(ParameterList node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // RelativeModuleName
        public override bool Walk(RelativeModuleName node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // TryStatementHandler
        public override bool Walk(TryStatementHandler node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // ErrorStatement
        public override bool Walk(ErrorStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }

        // DecoratorStatement
        public override bool Walk(DecoratorStatement node) { return Location >= node.Span.Start.Index && Location <= node.Span.End.Index; }
    }

}
