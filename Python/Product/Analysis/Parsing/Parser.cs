/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Parsing {

    public class Parser {
        // When this string is encountered as a comment, parser state is reset.
        public const string ResetStateMarker = "#reset_state_29F700BB398E4AE7931E8409F6EA4735";

        // immutable properties:
        private readonly Tokenization _tokenization;
        private readonly List<ErrorResult> _errorList;
        private readonly ErrorSink _errors;

        /// <summary>
        /// Language features initialized on parser construction and possibly updated during parsing. 
        /// The code can set the language features (e.g. "from __future__ import division").
        /// </summary>
        private FutureOptions _languageFeatures;
        private readonly PythonLanguageVersion _langVersion;
        // state:
        private TokenWithSpan _token, _eofToken;
        private readonly List<TokenWithSpan> _lookahead;
        private IEnumerator<TokenWithSpan> _tokens;

        private Stack<FunctionDefinition> _functions;
        private int _classDepth;
        private bool _fromFutureAllowed;
        private string _privatePrefix;
        private bool _parsingStarted, _allowIncomplete;
        private bool _inLoop, _inFinally, _isGenerator;
        private List<IndexSpan> _returnsWithValue;
        private int _errorCode;
        private readonly bool _verbatim;                            // true if we're in verbatim mode and the ASTs can be turned back into source code, preserving white space / comments
        private readonly bool _bindReferences;                      // true if we should bind the references in the ASTs
        private Dictionary<Node, Dictionary<object, object>> _attributes = new Dictionary<Node, Dictionary<object, object>>();  // attributes for each node, currently just round tripping information

        private bool _alwaysAllowContextDependentSyntax;

        #region Construction

        public Parser(Tokenization tokenization, ParserOptions options) {
            Contract.Assert(tokenization != null);
            options = options ?? ParserOptions.Default;

            _tokenization = tokenization;
            _errorList = new List<ErrorResult>();
            _errors = new CollectingErrorSink(_errorList);
            _lookahead = new List<TokenWithSpan>();
            _langVersion = _tokenization.LanguageVersion;
            _verbatim = _tokenization.Verbatim;
            _bindReferences = options.BindReferences;
            
            if (_langVersion.Is3x()) {
                // 3.x always does true division and absolute import
                _languageFeatures |= FutureOptions.TrueDivision | FutureOptions.AbsoluteImports;
            }

            Reset(FutureOptions.None);

            _privatePrefix = options.PrivatePrefix;
        }

        public static async Task<ParseResult> TokenizeAndParseExpressionAsync(
            string expression,
            PythonLanguageVersion languageVersion,
            TokenizerOptions tokenizerOptions = TokenizerOptions.None,
            ParserOptions parserOptions = null
        ) {
            var document = new StringLiteralDocument(expression);

            var tokenization = await Tokenization.TokenizeAsync(
                document,
                languageVersion,
                tokenizerOptions,
                Severity.Ignore
            );

            var parser = new Parser(tokenization, parserOptions);
            var result = parser.ParseTopExpression();
            tokenization.ClearRawTokens();
            return result;
        }

        public static async Task<ParseResult> TokenizeAndParseAsync(
            ISourceDocument document,
            PythonLanguageVersion languageVersion,
            TokenizerOptions tokenizerOptions = TokenizerOptions.None,
            ParserOptions parserOptions = null,
            Severity inconsistentIndentation = Severity.Ignore
        ) {
            var tokenization = await Tokenization.TokenizeAsync(
                document,
                languageVersion,
                tokenizerOptions,
                inconsistentIndentation
            );

            var parser = new Parser(tokenization, parserOptions);
            var result = parser.ParseFile();
            tokenization.ClearRawTokens();
            return result;
        }

        #endregion

        #region Public parser interface

        //single_input: Newline | simple_stmt | compound_stmt Newline
        //eval_input: testlist Newline* ENDMARKER
        //file_input: (Newline | stmt)* ENDMARKER
        public ParseResult ParseFile() {
            return ParseFileWorker();
        }

        //[stmt_list] Newline | compound_stmt Newline
        //stmt_list ::= simple_stmt (";" simple_stmt)* [";"]
        //compound_stmt: if_stmt | while_stmt | for_stmt | try_stmt | funcdef | classdef
        //Returns a simple or coumpound_stmt or null if input is incomplete
        /// <summary>
        /// Parse one or more lines of interactive input
        /// </summary>
        /// <returns>null if input is not yet valid but could be with more lines</returns>
        public ParseResult ParseInteractiveCode() {
            ParseResult result;
            bool parsingMultiLineCmpdStmt;
            bool isEmptyStmt = false;

            var state = ParseState.Invalid;

            StartParsing();
            Statement ret = InternalParseInteractiveInput(out parsingMultiLineCmpdStmt, out isEmptyStmt);

            if (isEmptyStmt) {
                return new ParseResult(null, ParseState.Empty, null);
            }

            if (_errorCode == 0) {
                if (parsingMultiLineCmpdStmt) {
                    state = ParseState.IncompleteStatement;
                } else {
                    state = ParseState.Complete;
                }
                
                result = CreateAst(ret);
                return new ParseResult(result.Tree, state, result.Errors);
            } else {
                if ((_errorCode & ErrorCodes.IncompleteMask) != 0) {
                    if ((_errorCode & ErrorCodes.IncompleteToken) != 0) {
                        state = ParseState.IncompleteToken;
                    } else if ((_errorCode & ErrorCodes.IncompleteStatement) != 0) {
                        if (parsingMultiLineCmpdStmt) {
                            state = ParseState.IncompleteStatement;
                        } else {
                            state = ParseState.IncompleteToken;
                        }
                    }
                }

                return new ParseResult(null, state, null);
            }
        }

        private ParseResult CreateAst(Statement ret) {
            var ast = new PythonAst(ret, _tokenization);
            ast.HasVerbatim = _verbatim;
            ast.PrivatePrefix = _privatePrefix;
            if (_token.Token != null) {
                ast.SetLoc(_token.Span);
            }
            if (_verbatim) {
                AddExtraVerbatimText(ast, Peek().LeadingWhitespace + Peek().Token.VerbatimImage);
            }
            foreach (var keyValue in _attributes) {
                foreach (var nodeAttr in keyValue.Value) {
                    ast.SetAttribute(keyValue.Key, nodeAttr.Key, nodeAttr.Value);
                }
            }
            
            PythonNameBinder.BindAst(_langVersion, ast, _errors, _bindReferences);

            return new ParseResult(ast, ParseState.Complete, _errorList.ToArray());
        }

        public ParseResult ParseSingleStatement() {
            StartParsing();

            MaybeEatNewLine();
            Statement statement = ParseStmt();
            EatEndOfInput();
            return CreateAst(statement);
        }

        public ParseResult ParseTopExpression() {
            // TODO: move from source unit  .TrimStart(' ', '\t')
            _alwaysAllowContextDependentSyntax = true;
            ReturnStatement ret = new ReturnStatement(ParseTestListAsExpression());
            _alwaysAllowContextDependentSyntax = false;
            ret.SetLoc(0, 0);

            return CreateAst(ret);
        }

        public int ErrorCode {
            get { return _errorCode; }
        }

        public void Reset(FutureOptions languageFeatures) {
            _languageFeatures = languageFeatures;
            _tokens = _tokenization.RawTokens.GetEnumerator();
            _token = TokenWithSpan.Empty;
            _lookahead.Clear();
            _eofToken = TokenWithSpan.Empty;
            _fromFutureAllowed = true;
            _classDepth = 0;
            _functions = null;
            _privatePrefix = null;

            _parsingStarted = false;
            _errorCode = 0;
        }

        public void Reset() {
            Reset(_languageFeatures);
        }

        #endregion

        #region Error Reporting

        private void ReportSyntaxError(
            TokenWithSpan t,
            int errorCode = ErrorCodes.SyntaxError,
            bool allowIncomplete = true
        ) {
            ReportSyntaxError(t.Token, t.Span, errorCode, allowIncomplete);
        }

        private void ReportSyntaxError(
            Token t,
            IndexSpan span,
            int errorCode = ErrorCodes.SyntaxError,
            bool allowIncomplete = true
        ) {
            var start = span.Start;
            var end = span.End;

            if (allowIncomplete &&
                (t.Kind == TokenKind.EndOfFile || t.Kind == TokenKind.Dedent || t.Kind == TokenKind.NLToken || t.Kind == TokenKind.Comment)) {
                errorCode |= ErrorCodes.IncompleteStatement;
            }

            ReportSyntaxError(start, end, GetErrorMessage(t, errorCode), errorCode);
        }

        private static string GetErrorMessage(Token t, int errorCode) {
            string msg;
            if ((errorCode & ~ErrorCodes.IncompleteMask) == ErrorCodes.IndentationError) {
                msg = "expected an indented block";
            } else if (t.Kind != TokenKind.EndOfFile) {
                msg = string.Format(CultureInfo.InvariantCulture, "unexpected token '{0}'", t.Image);
            } else {
                msg = "unexpected EOF while parsing";
            }

            return msg;
        }

        private void ReportSyntaxError() {
            var peek = Peek();
            ReportSyntaxError(peek.Span.Start, peek.Span.End, GetErrorMessage(peek.Token, ErrorCodes.SyntaxError));
        }

        private void ReportSyntaxError(string message) {
            var peek = Peek();
            ReportSyntaxError(peek.Span.Start, peek.Span.End, message);
        }

        internal void ReportSyntaxError(int start, int end, string message) {
            ReportSyntaxError(start, end, message, ErrorCodes.SyntaxError);
        }

        internal void ReportSyntaxError(int start, int end, string message, int errorCode) {
            // save the first one, the next error codes may be induced errors:
            if (_errorCode == 0) {
                _errorCode = errorCode;
            }
            _errors.Add(
                message,
                IndexSpan.FromPoints(start, end),
                errorCode,
                Severity.FatalError
            );
        }

        #endregion

        #region LL(1) Parsing

        private static bool IsPrivateName(string name) {
            return name.StartsWith("__") && !name.EndsWith("__");
        }

        private string FixName(string name) {
            if (_privatePrefix != null && IsPrivateName(name)) {
                name = "_" + _privatePrefix + name;
            }

            return name;
        }

        private Name ReadNameMaybeNone() {
            // peek for better error recovery
            var t = PeekToken();
            if (t == Tokens.NoneToken) {
                Next();
                return Name.None;
            }

            var n = TokenToName(t);
            if (n.HasName) {
                Next();
                return n;
            }

            ReportSyntaxError("syntax error");
            return Name.Empty;
        }

        struct Name {
            public readonly string RealName;
            public readonly string VerbatimName;

            public static readonly Name Empty = new Name();
            public static readonly Name Async = new Name("async", "async");
            public static readonly Name Await = new Name("await", "await");
            public static readonly Name None = new Name("None", "None");

            public Name(string name, string verbatimName) {
                RealName = name;
                VerbatimName = verbatimName;
            }

            public bool HasName {
                get {
                    return RealName != null;
                }
            }
        }

        private Name ReadName() {
            var n = TokenToName(PeekToken());
            if (n.HasName) {
                Next();
            } else {
                ReportSyntaxError();
            }
            return n;
        }

        private Name TokenToName(Token t) {
            if (!AllowAsyncAwaitSyntax) {
                if (t.Kind == TokenKind.KeywordAwait) {
                    return Name.Await;
                } else if (t.Kind == TokenKind.KeywordAsync) {
                    return Name.Async;
                }
            }
            var n = t as NameToken;
            if (n != null) {
                return new Name(FixName(n.Name), n.Name);
            }
            return Name.Empty;
        }

        private bool AllowReturnSyntax {
            get {
                return _alwaysAllowContextDependentSyntax ||
                    CurrentFunction != null;
            }
        }

        private bool AllowYieldSyntax {
            get {
                FunctionDefinition cf;
                return _alwaysAllowContextDependentSyntax ||
                    ((cf = CurrentFunction) != null && !cf.IsCoroutine);
            }
        }

        private bool AllowAsyncAwaitSyntax {
            get {
                FunctionDefinition cf;
                return _alwaysAllowContextDependentSyntax ||
                    ((cf = CurrentFunction) != null && cf.IsCoroutine);
            }
        }

        //stmt: simple_stmt | compound_stmt
        //compound_stmt: if_stmt | while_stmt | for_stmt | try_stmt | funcdef | classdef
        private Statement ParseStmt() {
            switch (PeekKind()) {
                case TokenKind.KeywordIf:
                    return ParseIfStmt();
                case TokenKind.KeywordWhile:
                    return ParseWhileStmt();
                case TokenKind.KeywordFor:
                    return ParseForStmt(isAsync: false);
                case TokenKind.KeywordTry:
                    return ParseTryStatement();
                case TokenKind.At:
                    return ParseDecorated();
                case TokenKind.KeywordDef:
                    return ParseFuncDef(isCoroutine: false);
                case TokenKind.KeywordClass:
                    return ParseClassDef();
                case TokenKind.KeywordWith:
                    return ParseWithStmt(isAsync: false);
                case TokenKind.KeywordAsync:
                    return ParseAsyncStmt();
                default:
                    return ParseSimpleStmt();
            }
        }

        private Statement ParseAsyncStmt() {
            if (PeekKind(TokenKind.KeywordDef, 2)) {
                Eat(TokenKind.KeywordAsync);
                return ParseFuncDef(isCoroutine: true);
            }

            if (!AllowAsyncAwaitSyntax) {
                // 'async', outside coroutine, and not followed by def, is a
                // regular name
                return ParseSimpleStmt();
            }

            Next();

            switch (PeekKind()) {
                case TokenKind.KeywordFor:
                    return ParseForStmt(isAsync: true);
                case TokenKind.KeywordWith:
                    return ParseWithStmt(isAsync: true);
            }

            ReportSyntaxError("syntax error");
            return ParseStmt();
        }

        //simple_stmt: small_stmt (';' small_stmt)* [';'] Newline
        private Statement ParseSimpleStmt() {
            Statement s;
            string newline = null;

            if (MaybeEat(TokenKind.Comment)) {
                // Create a node to attach the comment to.
                s = new CommentStatement(_token.Token.Image);
                s.SetLoc(_token.Span);
                if (_verbatim) {
                    AddPrecedingWhiteSpace(s, _token.LeadingWhitespace);
                }
                if (!MaybeEat(TokenKind.NewLine)) {
                    MaybeEat(TokenKind.NLToken);
                }
                return s;
            }

            s = ParseSmallStmt();

            if (MaybeEat(TokenKind.Semicolon)) {
                var itemWhiteSpace = MakeWhiteSpaceList();
                if (itemWhiteSpace != null) {
                    itemWhiteSpace.Add(_token.LeadingWhitespace);
                }

                var start = s.StartIndex;
                List<Statement> l = new List<Statement>();
                l.Add(s);
                while (true) {
                    if (MaybeEatNewLine(out newline) || MaybeEatEof()) {
                        break;
                    }

                    l.Add(ParseSmallStmt());

                    if (MaybeEatEof()) {
                        // implies a new line
                        break;
                    } else if (MaybeEat(TokenKind.Comment)) {
                        AddTrailingComment(l[l.Count - 1], _token.Token.VerbatimImage);
                    } else if (!MaybeEat(TokenKind.Semicolon)) {
                        EatNewLine(out newline);
                        break;
                    }
                    if (itemWhiteSpace != null) {
                        itemWhiteSpace.Add(_token.LeadingWhitespace);
                    }
                }
                Statement[] stmts = l.ToArray();

                SuiteStatement ret = new SuiteStatement(stmts);
                ret.SetLoc(start, stmts[stmts.Length - 1].EndIndex);
                if (itemWhiteSpace != null) {
                    AddListWhiteSpace(ret, itemWhiteSpace.ToArray());
                }
                if (newline != null) {
                    DeferWhitespace(newline);
                }
                return ret;
            } else if (MaybeEat(TokenKind.Comment)) {
                AddTrailingComment(s, _token.Token.VerbatimImage);
                if (MaybeEatNewLine(out newline) && _verbatim) {
                    DeferWhitespace(newline);
                }
            } else if (MaybeEatEof()) {
            } else if (EatNewLine(out newline)) {
                if (_verbatim) {
                    DeferWhitespace(newline);
                }
            } else {
                // error handling, make sure we're making forward progress
                Next();
                if (_verbatim) {
                    DeferWhitespace(_token.LeadingWhitespace + _token.Token.VerbatimImage);
                }
            }
            return s;
        }

        private bool MaybeEatEof() {
            if (PeekKind(TokenKind.EndOfFile)) {
                return true;
            }

            return false;
        }
        /*
        small_stmt: expr_stmt | print_stmt  | del_stmt | pass_stmt | flow_stmt | import_stmt | global_stmt | exec_stmt | assert_stmt

        del_stmt: 'del' exprlist
        pass_stmt: 'pass'
        flow_stmt: break_stmt | continue_stmt | return_stmt | raise_stmt | yield_stmt
        break_stmt: 'break'
        continue_stmt: 'continue'
        return_stmt: 'return' [testlist]
        yield_stmt: 'yield' testlist
        */
        private Statement ParseSmallStmt() {
            switch (PeekKind()) {
                case TokenKind.KeywordPrint:
                    return ParsePrintStmt();
                case TokenKind.KeywordPass:
                    return FinishSmallStmt(new EmptyStatement());
                case TokenKind.KeywordBreak:
                    if (!_inLoop) {
                        ReportSyntaxError("'break' outside loop");
                    }
                    return FinishSmallStmt(new BreakStatement());
                case TokenKind.KeywordContinue:
                    if (!_inLoop) {
                        ReportSyntaxError("'continue' not properly in loop");
                    } else if (_inFinally) {
                        ReportSyntaxError("'continue' not supported inside 'finally' clause");
                    }
                    return FinishSmallStmt(new ContinueStatement());
                case TokenKind.KeywordReturn:
                    return ParseReturnStmt();
                case TokenKind.KeywordFrom:
                    return ParseFromImportStmt();
                case TokenKind.KeywordImport:
                    return ParseImportStmt();
                case TokenKind.KeywordGlobal:
                    return ParseGlobalStmt();
                case TokenKind.KeywordNonlocal:
                    return ParseNonlocalStmt();
                case TokenKind.KeywordRaise:
                    return ParseRaiseStmt();
                case TokenKind.KeywordAssert:
                    return ParseAssertStmt();
                case TokenKind.KeywordExec:
                    return ParseExecStmt();
                case TokenKind.KeywordDel:
                    return ParseDelStmt();
                case TokenKind.KeywordYield:
                    return ParseYieldStmt();
                default:
                    return ParseExprStmt();
            }
        }

        // del_stmt: "del" target_list
        //  for error reporting reasons we allow any expression and then report the bad
        //  delete node when it fails.  This is the reason we don't call ParseTargetList.
        private Statement ParseDelStmt() {
            var token = Next();
            string delWhiteSpace = _token.LeadingWhitespace;
            var start = GetStart();
            List<string> itemWhiteSpace;

            DelStatement ret;
            if (PeekKind(TokenKind.NewLine) || PeekKind(TokenKind.EndOfFile)) {
                ReportSyntaxError(token.Span.Start, token.Span.End, "expected expression after del");
                ret = new DelStatement(new Expression[0]);
            } else {
                List<Expression> l = ParseExprList(out itemWhiteSpace);
                foreach (Expression e in l) {
                    if (e is ErrorExpression) {
                        continue;
                    }
                    string delError = e.CheckDelete();
                    if (delError != null) {
                        ReportSyntaxError(e.StartIndex, e.EndIndex, delError, ErrorCodes.SyntaxError);
                    }
                }

                ret = new DelStatement(l.ToArray());
                if (itemWhiteSpace != null) {
                    AddListWhiteSpace(ret, itemWhiteSpace.ToArray());
                }
            }

            ret.SetLoc(start, GetEnd());
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, delWhiteSpace);
            }
            return ret;
        }

        private Statement ParseReturnStmt() {
            if (!AllowReturnSyntax) {
                ReportSyntaxError("'return' outside function");
            }
            var returnToken = Next();
            string returnWhitespace = _token.LeadingWhitespace;
            Expression expr = null;
            var start = GetStart();
            if (!NeverTestToken(PeekToken())) {
                expr = ParseTestListAsExpr();
            }

            if (expr != null && _langVersion < PythonLanguageVersion.V33) {
                if (_isGenerator) {
                    ReportSyntaxError(returnToken.Span.Start, expr.EndIndex, "'return' with argument inside generator");
                } else {
                    if (_returnsWithValue == null) {
                        _returnsWithValue = new List<IndexSpan>();
                    }
                    _returnsWithValue.Add(new IndexSpan(returnToken.Span.Start, expr.EndIndex - returnToken.Span.Start));
                }
            }

            ReturnStatement ret = new ReturnStatement(expr);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, returnWhitespace);
            }
            ret.SetLoc(start, GetEnd());
            return ret;
        }

        private Statement FinishSmallStmt(Statement stmt) {
            Next();
            stmt.SetLoc(GetStart(), GetEnd());
            if (_verbatim) {
                AddPrecedingWhiteSpace(stmt, _token.LeadingWhitespace);
            }
            return stmt;
        }


        private Statement ParseYieldStmt() {
            // For yield statements, continue to enforce that it's currently in a function. 
            // This gives us better syntax error reporting for yield-statements than for yield-expressions.
            if (!AllowYieldSyntax) {
                if (AllowAsyncAwaitSyntax) {
                    ReportSyntaxError("'yield' inside async function");
                } else {
                    ReportSyntaxError("misplaced yield");
                }
            }

            _isGenerator = true;
            if (_returnsWithValue != null && _langVersion < PythonLanguageVersion.V33) {
                foreach (var span in _returnsWithValue) {
                    ReportSyntaxError(span.Start, span.End, "'return' with argument inside generator");
                }
            }

            Eat(TokenKind.KeywordYield);

            // See Pep 342: a yield statement is now just an expression statement around a yield expression.
            Expression e = ParseYieldExpression();
            Debug.Assert(e != null); // caller already verified we have a yield.

            Statement s = new ExpressionStatement(e);
            s.SetLoc(e.IndexSpan);
            return s;
        }

        /// <summary>
        /// Peek if the next token is a 'yield' and parse a yield or yield from expression. Else return null.
        /// 
        /// Called w/ yield already eaten.
        /// </summary>
        /// <returns>A yield or yield from expression if present, else null.</returns>
        // yield_expression: "yield" [expression_list] 
        private Expression ParseYieldExpression() {
            // Mark that this function is actually a generator.
            // If we're in a generator expression, then we don't have a function yet.
            //    g=((yield i) for i in range(5))
            // In that case, the genexp will mark IsGenerator. 
            FunctionDefinition current = CurrentFunction;
            if (current != null && !current.IsCoroutine) {
                current.IsGenerator = true;
            }
            string whitespace = _token.LeadingWhitespace;

            var start = GetStart();

            // Parse expression list after yield. This can be:
            // 1) empty, in which case it becomes 'yield None'
            // 2) a single expression
            // 3) multiple expression, in which case it's wrapped in a tuple.
            // 4) 'from', in which case we expect a single expression and return YieldFromExpression
            Expression yieldResult;
            
            bool isYieldFrom = PeekKind(TokenKind.KeywordFrom);
            bool suppressSyntaxError = false;
            string fromWhitespace = string.Empty;

            if (isYieldFrom) {
                if (_langVersion < PythonLanguageVersion.V33) {
                    // yield from added to 3.3
                    ReportSyntaxError("invalid syntax");
                    suppressSyntaxError = true;
                }
                Next();
                fromWhitespace = _token.LeadingWhitespace;
            }

            bool trailingComma;
            List<string> itemWhiteSpace;
            List<Expression> l = ParseTestListAsExpr(null, out itemWhiteSpace, out trailingComma);                
            if (l.Count == 0) {
                if (_langVersion < PythonLanguageVersion.V25 && !suppressSyntaxError) {
                    // 2.4 doesn't allow plain yield
                    ReportSyntaxError("invalid syntax");
                } else if (isYieldFrom && !suppressSyntaxError) {
                    // yield from requires one expression
                    ReportSyntaxError("invalid syntax");
                }
                // Check empty expression and convert to 'none'
                yieldResult = new ConstantExpression(null);
            } else if (l.Count != 1) {
                if (isYieldFrom && !suppressSyntaxError) {
                    // yield from requires one expression
                    ReportSyntaxError(l[0].StartIndex, l[l.Count - 1].EndIndex, "invalid syntax");
                }
                // make a tuple
                yieldResult = MakeTupleOrExpr(l, itemWhiteSpace, trailingComma, true);
            } else {
                // just take the single expression
                yieldResult = l[0];
            }

            Expression yieldExpression;
            if (isYieldFrom) {
                yieldExpression = new YieldFromExpression(yieldResult);
            } else {
                yieldExpression = new YieldExpression(yieldResult);
            }
            if (_verbatim) {
                AddPrecedingWhiteSpace(yieldExpression, whitespace);
                if (!string.IsNullOrEmpty(fromWhitespace)) {
                    AddSecondPrecedingWhiteSpace(yieldExpression, fromWhitespace);
                }

                if (l.Count == 0) {
                    AddIsAltForm(yieldExpression);
                } else if (l.Count == 1 && trailingComma) {
                    AddListWhiteSpace(yieldExpression, itemWhiteSpace.ToArray());
                }
            }
            yieldExpression.SetLoc(start, GetEnd());
            return yieldExpression;

        }

        private Statement FinishAssignments(Expression right) {
            List<Expression> left = null;
            List<string> assignWhiteSpace = MakeWhiteSpaceList();
            Expression singleLeft = null;

            while (MaybeEat(TokenKind.Assign)) {
                if (assignWhiteSpace != null) {
                    assignWhiteSpace.Add(_token.LeadingWhitespace);
                }
                string assignError = right.CheckAssign();
                if (assignError != null) {
                    ReportSyntaxError(right.StartIndex, right.EndIndex, assignError, ErrorCodes.SyntaxError | ErrorCodes.NoCaret);
                }

                if (singleLeft == null) {
                    singleLeft = right;
                } else {
                    if (left == null) {
                        left = new List<Expression>();
                        left.Add(singleLeft);
                    }
                    left.Add(right);
                }

                if (_langVersion >= PythonLanguageVersion.V25 && PeekKind(TokenKind.KeywordYield)) {
                    if (!AllowYieldSyntax && AllowAsyncAwaitSyntax) {
                        ReportSyntaxError("'yield' inside async function");
                    }
                    Eat(TokenKind.KeywordYield);
                    right = ParseYieldExpression();
                } else {
                    right = ParseTestListAsExpr();
                }
            }

            AssignmentStatement assign;
            if (left != null) {
                Debug.Assert(left.Count > 0);

                assign = new AssignmentStatement(left.ToArray(), right);
                assign.SetLoc(left[0].StartIndex, right.EndIndex);
            } else {
                Debug.Assert(singleLeft != null);

                assign = new AssignmentStatement(new[] { singleLeft }, right);
                assign.SetLoc(singleLeft.StartIndex, right.EndIndex);
            }
            if (assignWhiteSpace != null) {
                AddListWhiteSpace(assign, assignWhiteSpace.ToArray());
            }
            return assign;
        }

        // expr_stmt: expression_list
        // expression_list: expression ( "," expression )* [","] 
        // assignment_stmt: (target_list "=")+ (expression_list | yield_expression) 
        // augmented_assignment_stmt ::= target augop (expression_list | yield_expression) 
        // augop: '+=' | '-=' | '*=' | '/=' | '%=' | '**=' | '>>=' | '<<=' | '&=' | '^=' | '|=' | '//='
        private Statement ParseExprStmt() {
            Expression ret = ParseTestListAsExpr();

            if (PeekKind(TokenKind.Assign)) {
                if (_langVersion >= PythonLanguageVersion.V30) {
                    SequenceExpression seq = ret as SequenceExpression;
                    bool hasStar = false;
                    if (seq != null) {
                        for (int i = 0; i < seq.Items.Count; i++) {
                            if (seq.Items[i] is StarredExpression) {
                                if (hasStar) {
                                    ReportSyntaxError(seq.Items[i].StartIndex, seq.Items[i].EndIndex, "two starred expressions in assignment");
                                }
                                hasStar = true;
                            }
                        }
                    }
                }

                return FinishAssignments(ret);
            } else {
                PythonOperator op = GetAssignOperator(PeekToken());
                if (op != PythonOperator.None) {
                    Next();
                    string whiteSpace = _token.LeadingWhitespace;
                    Expression rhs;

                    if (_langVersion >= PythonLanguageVersion.V25 && PeekKind(TokenKind.KeywordYield)) {
                        if (!AllowYieldSyntax && AllowAsyncAwaitSyntax) {
                            ReportSyntaxError("'yield' inside async function");
                        }
                        Eat(TokenKind.KeywordYield);
                        rhs = ParseYieldExpression();
                    } else {
                        rhs = ParseTestListAsExpr();
                    }

                    string assignError = ret.CheckAugmentedAssign();
                    if (assignError != null) {
                        ReportSyntaxError(ret.StartIndex, ret.EndIndex, assignError);
                    }

                    AugmentedAssignStatement aug = new AugmentedAssignStatement(op, ret, rhs);
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(aug, whiteSpace);
                    }
                    aug.SetLoc(ret.StartIndex, GetEnd());
                    return aug;
                } else {
                    Statement stmt = new ExpressionStatement(ret);
                    stmt.SetLoc(ret.IndexSpan);
                    return stmt;
                }
            }
        }

        private PythonOperator GetAssignOperator(Token t) {
            switch (t.Kind) {
                case TokenKind.AddEqual: return PythonOperator.Add;
                case TokenKind.SubtractEqual: return PythonOperator.Subtract;
                case TokenKind.MultiplyEqual: return PythonOperator.Multiply;
                case TokenKind.MatMultiplyEqual: return PythonOperator.MatMultiply;
                case TokenKind.DivideEqual: return TrueDivision ? PythonOperator.TrueDivide : PythonOperator.Divide;
                case TokenKind.ModEqual: return PythonOperator.Mod;
                case TokenKind.BitwiseAndEqual: return PythonOperator.BitwiseAnd;
                case TokenKind.BitwiseOrEqual: return PythonOperator.BitwiseOr;
                case TokenKind.ExclusiveOrEqual: return PythonOperator.Xor;
                case TokenKind.LeftShiftEqual: return PythonOperator.LeftShift;
                case TokenKind.RightShiftEqual: return PythonOperator.RightShift;
                case TokenKind.PowerEqual: return PythonOperator.Power;
                case TokenKind.FloorDivideEqual: return PythonOperator.FloorDivide;
                default: return PythonOperator.None;
            }
        }


        private PythonOperator GetBinaryOperator(OperatorToken token) {
            switch (token.Kind) {
                case TokenKind.Add: return PythonOperator.Add;
                case TokenKind.Subtract: return PythonOperator.Subtract;
                case TokenKind.Multiply: return PythonOperator.Multiply;
                case TokenKind.MatMultiply: return PythonOperator.MatMultiply;
                case TokenKind.Divide: return TrueDivision ? PythonOperator.TrueDivide : PythonOperator.Divide;
                case TokenKind.Mod: return PythonOperator.Mod;
                case TokenKind.BitwiseAnd: return PythonOperator.BitwiseAnd;
                case TokenKind.BitwiseOr: return PythonOperator.BitwiseOr;
                case TokenKind.ExclusiveOr: return PythonOperator.Xor;
                case TokenKind.LeftShift: return PythonOperator.LeftShift;
                case TokenKind.RightShift: return PythonOperator.RightShift;
                case TokenKind.Power: return PythonOperator.Power;
                case TokenKind.FloorDivide: return PythonOperator.FloorDivide;
                default:
                    Debug.Assert(false, "Unreachable");
                    return PythonOperator.None;
            }
        }


        // import_stmt: 'import' module ['as' name"] (',' module ['as' name])*        
        // name: identifier
        private ImportStatement ParseImportStmt() {
            Eat(TokenKind.KeywordImport);
            string whitespace = _token.LeadingWhitespace;
            var start = GetStart();

            List<string> asNameWhiteSpace = MakeWhiteSpaceList();
            List<ModuleName> l = new List<ModuleName>();
            List<NameExpression> las = new List<NameExpression>();
            var modName = ParseModuleName();
            var commaWhiteSpace = MakeWhiteSpaceList();
            if (modName.Names.Count > 0) {
                l.Add(modName);
                las.Add(MaybeParseAsName(asNameWhiteSpace));
                while (MaybeEat(TokenKind.Comma)) {
                    if (commaWhiteSpace != null) {
                        commaWhiteSpace.Add(_token.LeadingWhitespace);
                    }
                    l.Add(ParseModuleName());
                    las.Add(MaybeParseAsName(asNameWhiteSpace));
                }
            }
            ModuleName[] names = l.ToArray();
            var asNames = las.ToArray();

            ImportStatement ret = new ImportStatement(names, asNames, AbsoluteImports);
            if (_verbatim) {
                AddListWhiteSpace(ret, commaWhiteSpace.ToArray());
                AddNamesWhiteSpace(ret, asNameWhiteSpace.ToArray());
                AddPrecedingWhiteSpace(ret, whitespace);
            }
            ret.SetLoc(start, GetEnd());
            return ret;
        }

        // module: (identifier '.')* identifier
        private ModuleName ParseModuleName() {
            var start = GetStart();
            List<string> dotWhiteSpace;
            ModuleName ret = new ModuleName(ReadDottedName(out dotWhiteSpace));
            if (_verbatim) {
                AddNamesWhiteSpace(ret, dotWhiteSpace.ToArray());
            }

            if (ret.Names.Count > 0) {
                start = ret.Names[0].StartIndex;
            }
            ret.SetLoc(start, GetEnd());
            return ret;
        }

        private static NameExpression[] EmptyNames = new NameExpression[0];

        // relative_module: "."* module | "."+
        private ModuleName ParseRelativeModuleName() {
            var start = GetStart();
            bool isStartSetCorrectly = false;

            int dotCount = 0;
            List<string> dotWhiteSpace = MakeWhiteSpaceList();
            for (; ; ) {
                if (MaybeEat(TokenKind.Dot)) {
                    if (dotWhiteSpace != null) {
                        dotWhiteSpace.Add(_token.LeadingWhitespace); 
                    }
                    dotCount++;
                } else if (MaybeEat(TokenKind.Ellipsis)) {
                    if (dotWhiteSpace != null) {
                        dotWhiteSpace.Add(_token.LeadingWhitespace);
                        dotWhiteSpace.Add("");
                        dotWhiteSpace.Add("");
                    }
                    dotCount += 3;
                } else {
                    break;
                }
                if (!isStartSetCorrectly) {
                    start = GetStart();
                    isStartSetCorrectly = true;
                }
            }

            List<string> nameWhiteSpace = null;
            NameExpression[] names = EmptyNames;
            if (PeekToken() is NameToken) {
                names = ReadDottedName(out nameWhiteSpace);
                if (!isStartSetCorrectly && names.Length > 0) {
                    start = names[0].StartIndex;
                    isStartSetCorrectly = true;
                }
            }

            ModuleName ret;
            if (dotCount > 0) {
                ret = new RelativeModuleName(names, dotCount);
                if (_verbatim) {
                    if (nameWhiteSpace != null) {
                        AddNamesWhiteSpace(ret, nameWhiteSpace.ToArray());
                    }
                    AddListWhiteSpace(ret, dotWhiteSpace.ToArray());
                }
            } else {
                if (names.Length == 0) {
                    ReportSyntaxError("missing module name");
                }
                ret = new ModuleName(names);
                if (nameWhiteSpace != null) {
                    AddNamesWhiteSpace(ret, nameWhiteSpace.ToArray());
                }
            }

            ret.SetLoc(start, GetEnd());
            return ret;
        }

        private NameExpression[] ReadDottedName(out List<string> dotWhiteSpace) {
            List<NameExpression> l = new List<NameExpression>();
            dotWhiteSpace = MakeWhiteSpaceList();

            var name = ReadName();
            if (name.HasName) {
                var nameExpr = MakeName(name);
                nameExpr.SetLoc(GetStart(), GetEnd());
                l.Add(nameExpr);

                if (_verbatim) {
                    dotWhiteSpace.Add(_token.LeadingWhitespace);
                }
                while (MaybeEat(TokenKind.Dot)) {
                    if (dotWhiteSpace != null) {
                        dotWhiteSpace.Add(_token.LeadingWhitespace);
                    }
                    name = ReadName();
                    nameExpr = MakeName(name);
                    nameExpr.SetLoc(GetStart(), GetEnd());
                    l.Add(nameExpr);
                    if (_verbatim) {
                        dotWhiteSpace.Add(_token.LeadingWhitespace);
                    }
                }
            }
            return l.ToArray();
        }


        // 'from' relative_module 'import' identifier ['as' name] (',' identifier ['as' name]) *
        // 'from' relative_module 'import' '(' identifier ['as' name] (',' identifier ['as' name])* [','] ')'        
        // 'from' module 'import' "*"                                        
        private FromImportStatement ParseFromImportStmt() {
            Eat(TokenKind.KeywordFrom);
            string fromWhiteSpace = _token.LeadingWhitespace;
            var start = GetStart();
            ModuleName dname = ParseRelativeModuleName();

            bool ateImport = Eat(TokenKind.KeywordImport);
            string importWhiteSpace = _token.LeadingWhitespace;

            bool ateParen = ateImport && MaybeEat(TokenKind.LeftParenthesis);
            string parenWhiteSpace = ateParen ? _token.LeadingWhitespace : null;

            NameExpression/*!*/[] names;
            NameExpression[] asNames;
            bool fromFuture = false;

            List<string> namesWhiteSpace = null;
            if (ateImport) {
                if (MaybeEat(TokenKind.Multiply)) {
                    if (_langVersion.Is3x() && ((_functions != null && _functions.Count > 0) || _classDepth > 0)) {
                        ReportSyntaxError(start, GetEnd(), "import * only allowed at module level");
                    }

                    if (_verbatim) {
                        namesWhiteSpace = new List<string>() { _token.LeadingWhitespace };
                    }
                    names = new[] { new NameExpression("*") };
                    asNames = null;
                } else {
                    List<NameExpression/*!*/> l = new List<NameExpression>();
                    List<NameExpression> las = new List<NameExpression>();
                    ParseAsNameList(l, las, out namesWhiteSpace);

                    names = l.ToArray();
                    asNames = las.ToArray();
                }
            } else {
                names = EmptyNames;
                asNames = EmptyNames;
            }

            // Process from __future__ statement
            if (dname.Names.Count == 1 && dname.Names[0].Name == "__future__") {
                fromFuture = ProcessFutureStatements(start, names, fromFuture);
            }

            bool ateRightParen = false;
            if (ateParen) {
                ateRightParen = Eat(TokenKind.RightParenthesis);
            }

            FromImportStatement ret = new FromImportStatement(dname, names, asNames, fromFuture, AbsoluteImports);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, fromWhiteSpace);
                AddSecondPrecedingWhiteSpace(ret, importWhiteSpace);
                if (namesWhiteSpace != null) {
                    AddNamesWhiteSpace(ret, namesWhiteSpace.ToArray());
                }
                if (ateParen) {
                    AddThirdPrecedingWhiteSpace(ret, parenWhiteSpace);
                    AddFourthPrecedingWhiteSpace(ret, _token.LeadingWhitespace);
                    if (!ateRightParen) {
                        AddErrorMissingCloseGrouping(ret);
                    }
                } else {
                    AddIsAltForm(ret);
                }
                if (!ateImport) {
                    AddErrorIsIncompleteNode(ret);
                }
                
            }
            ret.SetLoc(start, GetEnd());
            return ret;
        }

        private bool ProcessFutureStatements(int start, NameExpression/*!*/[] names, bool fromFuture) {
            if (!_fromFutureAllowed) {
                ReportSyntaxError(start, GetEnd(), "from __future__ imports must occur at the beginning of the file");
            }
            if (names.Length == 1 && names[0].Name == "*") {
                ReportSyntaxError(start, GetEnd(), "future statement does not support import *");
            }
            fromFuture = true;
            foreach (var name in names) {
                if (name.Name == "nested_scopes") {

                    // v2.4
                } else if (name.Name == "division") {
                    _languageFeatures |= FutureOptions.TrueDivision;
                } else if (name.Name == "generators") {

                    // v2.5:
                } else if (_langVersion >= PythonLanguageVersion.V25 && name.Name == "with_statement") {
                    _languageFeatures |= FutureOptions.WithStatement;
                } else if (_langVersion >= PythonLanguageVersion.V25 && name.Name == "absolute_import") {
                    _languageFeatures |= FutureOptions.AbsoluteImports;

                    // v2.6:
                } else if (_langVersion >= PythonLanguageVersion.V26 && name.Name == "print_function") {
                    _languageFeatures |= FutureOptions.PrintFunction;
                } else if (_langVersion >= PythonLanguageVersion.V26 && name.Name == "unicode_literals") {
                    _languageFeatures |= FutureOptions.UnicodeLiterals;

                    // v3.5:
                } else if (_langVersion >= PythonLanguageVersion.V35 && name.Name == "generator_stop") {
                    // No behavior change, but we don't want to display an error
                } else {
                    string strName = name.Name;

                    if (strName != "braces") {
                        ReportSyntaxError(start, GetEnd(), "future feature is not defined: " + strName);
                    } else {
                        // match CPython error message
                        ReportSyntaxError(start, GetEnd(), "not a chance");
                    }
                }
            }
            return fromFuture;
        }

        // import_as_name (',' import_as_name)*
        private void ParseAsNameList(List<NameExpression/*!*/> l, List<NameExpression> las, out List<string> asNamesWhiteSpace) {
            asNamesWhiteSpace = MakeWhiteSpaceList();
            
            var name = ReadName();
            var nameExpr = MakeName(name);
            nameExpr.SetLoc(GetStart(), GetEnd());
            l.Add(nameExpr);
            if (_verbatim) {
                asNamesWhiteSpace.Add(name.HasName ? _token.LeadingWhitespace : "");
            }

            las.Add(MaybeParseAsName(asNamesWhiteSpace));
            while (MaybeEat(TokenKind.Comma)) {
                if (asNamesWhiteSpace != null) {
                    asNamesWhiteSpace.Add(_token.LeadingWhitespace);
                }

                if (PeekKind(TokenKind.RightParenthesis)) return;  // the list is allowed to end with a ,
                name = ReadName();
                nameExpr = MakeName(name);
                nameExpr.SetLoc(GetStart(), GetEnd());
                l.Add(nameExpr);
                if (_verbatim) {
                    asNamesWhiteSpace.Add(_token.LeadingWhitespace);
                }
                las.Add(MaybeParseAsName(asNamesWhiteSpace));
            }
        }

        //import_as_name: NAME [NAME NAME]
        //dotted_as_name: dotted_name [NAME NAME]
        private NameExpression MaybeParseAsName(List<string> asNameWhiteSpace) {
            if (MaybeEat(TokenKind.KeywordAs) || MaybeEatName("as")) {
                if (_verbatim) {
                    asNameWhiteSpace.Add(_token.LeadingWhitespace);
                }
                var res = ReadName();
                if (_verbatim) {
                    asNameWhiteSpace.Add(_token.LeadingWhitespace);
                }
                var nameExpr = MakeName(res);
                nameExpr.SetLoc(GetStart(), GetEnd());
                return nameExpr;
            }

            return null;
        }

        //exec_stmt: 'exec' expr ['in' expression [',' expression]]
        private ExecStatement ParseExecStmt() {
            Eat(TokenKind.KeywordExec);
            string execWhiteSpace = _token.LeadingWhitespace;
            var start = GetStart();
            Expression code, locals = null, globals = null;
            code = ParseExpr();
            string inWhiteSpace = null, commaWhiteSpace = null;
            if (MaybeEat(TokenKind.KeywordIn)) {
                inWhiteSpace = _token.LeadingWhitespace;
                globals = ParseExpression();
                if (MaybeEat(TokenKind.Comma)) {
                    commaWhiteSpace = _token.LeadingWhitespace;
                    locals = ParseExpression();
                }
            }
            var codeTuple = code as TupleExpression;
            if (_langVersion.Is2x() && codeTuple != null) {
                if (codeTuple.Items != null) {
                    if (codeTuple.Items.Count >= 3) {
                        locals = codeTuple.Items[2];
                    }
                    if (codeTuple.Items.Count >= 2) {
                        globals = codeTuple.Items[1];
                    }
                    if (codeTuple.Items.Count >= 1) {
                        code = codeTuple.Items[0];
                    }
                }
            }
            ExecStatement ret = new ExecStatement(code, locals, globals);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, execWhiteSpace);
                AddSecondPrecedingWhiteSpace(ret, inWhiteSpace);
                AddThirdPrecedingWhiteSpace(ret, commaWhiteSpace);
            }
            ret.SetLoc(start, GetEnd());
            return ret;
        }

        //global_stmt: 'global' NAME (',' NAME)*
        private GlobalStatement ParseGlobalStmt() {
            Eat(TokenKind.KeywordGlobal);
            var start = GetStart();
            string globalWhiteSpace = _token.LeadingWhitespace;
            List<string> commaWhiteSpace;
            List<string> namesWhiteSpace;
            
            var l = ReadNameList(out commaWhiteSpace, out namesWhiteSpace);
            var names = l.ToArray();
            GlobalStatement ret = new GlobalStatement(names);
            ret.SetLoc(start, GetEnd());
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, globalWhiteSpace);
                AddListWhiteSpace(ret, commaWhiteSpace.ToArray());
                AddNamesWhiteSpace(ret, namesWhiteSpace.ToArray());
            }
            return ret;
        }

        private NonlocalStatement ParseNonlocalStmt() {
            if (_functions != null && _functions.Count == 0 && _classDepth == 0) {
                ReportSyntaxError("nonlocal declaration not allowed at module level");
            }

            Eat(TokenKind.KeywordNonlocal);
            string localWhiteSpace = _token.LeadingWhitespace;
            var start = GetStart();
            List<string> commaWhiteSpace;
            List<string> namesWhiteSpace;
            
            var l = ReadNameList(out commaWhiteSpace, out namesWhiteSpace);
            var names = l.ToArray();
            NonlocalStatement ret = new NonlocalStatement(names);
            ret.SetLoc(start, GetEnd());
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, localWhiteSpace);
                AddListWhiteSpace(ret, commaWhiteSpace.ToArray());
                AddNamesWhiteSpace(ret, namesWhiteSpace.ToArray());
            }
            return ret;
        }

        private List<NameExpression> ReadNameList(out List<string> commaWhiteSpace, out List<string> namesWhiteSpace) {
            List<NameExpression> l = new List<NameExpression>();
            commaWhiteSpace = MakeWhiteSpaceList();
            namesWhiteSpace = MakeWhiteSpaceList();

            var name = ReadName();
            if (name.HasName) {
                var nameExpr = MakeName(name);
                nameExpr.SetLoc(GetStart(), GetEnd());
                l.Add(nameExpr);
                if (_verbatim) {
                    namesWhiteSpace.Add(_token.LeadingWhitespace);
                }
                while (MaybeEat(TokenKind.Comma)) {
                    if (commaWhiteSpace != null) {
                        commaWhiteSpace.Add(_token.LeadingWhitespace);
                    }
                    name = ReadName();
                    nameExpr = MakeName(name);
                    nameExpr.SetLoc(GetStart(), GetEnd());
                    l.Add(nameExpr);
                    if (_verbatim) {
                        namesWhiteSpace.Add(_token.LeadingWhitespace);
                    }
                }
            }
            return l;
        }

        //raise_stmt: 'raise' [expression [',' expression [',' expression]]]
        private RaiseStatement ParseRaiseStmt() {
            Eat(TokenKind.KeywordRaise);
            string raiseWhiteSpace = _token.LeadingWhitespace;
            string commaWhiteSpace = null, secondCommaWhiteSpace = null;
            var start = GetStart();
            Expression type = null, value = null, traceback = null, cause = null;
            bool isFromForm = false;

            if (!NeverTestToken(PeekToken())) {
                type = ParseExpression();
                
                if (MaybeEat(TokenKind.Comma)) {
                    var commaStart = GetStart();
                    commaWhiteSpace = _token.LeadingWhitespace;
                    value = ParseExpression();
                    if (_langVersion.Is3x()) {
                        ReportSyntaxError(commaStart, GetEnd(), "invalid syntax, only exception value is allowed in 3.x.");
                    }
                    if (MaybeEat(TokenKind.Comma)) {
                        secondCommaWhiteSpace = _token.LeadingWhitespace;
                        traceback = ParseExpression();
                    }
                } else if (MaybeEat(TokenKind.KeywordFrom)) {
                    commaWhiteSpace = _token.LeadingWhitespace;
                    var fromStart = GetStart();
                    cause = ParseExpression();
                    isFromForm = true;

                    if (_langVersion.Is2x()) {
                       ReportSyntaxError(fromStart, cause.EndIndex, "invalid syntax, from cause not allowed in 2.x.");
                    }
                }

            }
            RaiseStatement ret = new RaiseStatement(type, value, traceback, cause);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, raiseWhiteSpace);
                AddSecondPrecedingWhiteSpace(ret, commaWhiteSpace);
                AddThirdPrecedingWhiteSpace(ret, secondCommaWhiteSpace);
                if (isFromForm) {
                    AddIsAltForm(ret);
                }
            }

            ret.SetLoc(start, GetEnd());
            return ret;
        }

        //assert_stmt: 'assert' expression [',' expression]
        private AssertStatement ParseAssertStmt() {
            Eat(TokenKind.KeywordAssert);
            string whiteSpace = _token.LeadingWhitespace;
            var start = GetStart();
            Expression expr = ParseExpression();
            Expression message = null;
            string commaWhiteSpace = null;
            if (MaybeEat(TokenKind.Comma)) {
                commaWhiteSpace = _token.LeadingWhitespace;
                message = ParseExpression();
            }
            AssertStatement ret = new AssertStatement(expr, message);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, whiteSpace);
                AddSecondPrecedingWhiteSpace(ret, commaWhiteSpace);
            }
            ret.SetLoc(start, GetEnd());
            return ret;
        }

        //print_stmt: 'print' ( [ expression (',' expression)* [','] ] | '>>' expression [ (',' expression)+ [','] ] )
        private PrintStatement ParsePrintStmt() {
            Eat(TokenKind.KeywordPrint);
            string printWhiteSpace = _token.LeadingWhitespace;
            var start = GetStart();
            Expression dest = null;
            PrintStatement ret;

            string rightShiftWhiteSpace = null;
            string theCommaWhiteSpace = null;
            bool needNonEmptyTestList = false;
            int end = 0;
            if (MaybeEat(TokenKind.RightShift)) {
                rightShiftWhiteSpace = _token.LeadingWhitespace;
                dest = ParseExpression();
                if (MaybeEat(TokenKind.Comma)) {
                    theCommaWhiteSpace = _token.LeadingWhitespace;
                    needNonEmptyTestList = true;
                    end = GetEnd();
                } else {
                    ret = new PrintStatement(dest, new Expression[0], false);
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(ret, printWhiteSpace);
                        AddSecondPrecedingWhiteSpace(ret, rightShiftWhiteSpace);
                    }
                    ret.SetLoc(start, GetEnd());
                    return ret;
                }
            }

            bool trailingComma = false;
            List<string> commaWhiteSpace = null;

            Expression[] exprs;
            if (!NeverTestToken(PeekToken())) {
                var expr = ParseExpression();
                if (!MaybeEat(TokenKind.Comma)) {
                    exprs = new[] { expr };
                } else {
                    List<Expression> exprList = ParseTestListAsExpr(expr, out commaWhiteSpace, out trailingComma);
                    exprs = exprList.ToArray();
                }
            } else {
                if (needNonEmptyTestList) {
                    ReportSyntaxError(start, end, "print statement expected expression to be printed");
                    exprs = new[] { Error("") };
                } else {
                    exprs = new Expression[0];
                }
            }
            
            ret = new PrintStatement(dest, exprs, trailingComma);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, printWhiteSpace);
                AddSecondPrecedingWhiteSpace(ret, rightShiftWhiteSpace);
                AddThirdPrecedingWhiteSpace(ret, theCommaWhiteSpace);
                if (commaWhiteSpace != null) {
                    AddListWhiteSpace(ret, commaWhiteSpace.ToArray());
                }
            }
            ret.SetLoc(start, GetEnd());
            return ret;
        }

        private string SetPrivatePrefix(string name) {
            string oldPrefix = _privatePrefix;

            _privatePrefix = GetPrivatePrefix(name);

            return oldPrefix;
        }

        internal static string GetPrivatePrefix(string name) {
            // Remove any leading underscores before saving the prefix
            if (name != null) {
                for (int i = 0; i < name.Length; i++) {
                    if (name[i] != '_') {
                        return name.Substring(i);
                    }
                }
            }
            // Name consists of '_'s only, no private prefix mapping
            return null;
        }

        private ErrorExpression Error(string verbatimImage = null, Expression preceeding = null) {
            var res = new ErrorExpression(verbatimImage, preceeding);
            res.SetLoc(GetStart(), GetEnd());
            return res;
        }

        private ErrorStatement ErrorStmt(string verbatimImage = null, params Statement[] preceeding) {
            var res = new ErrorStatement(preceeding);
            if (verbatimImage != null) {
                AddVerbatimImage(res, verbatimImage);
            }

            res.SetLoc(GetStart(), GetEnd());
            return res;
        }

        //classdef: 'class' NAME ['(' testlist ')'] ':' suite
        private Statement ParseClassDef() {
            Eat(TokenKind.KeywordClass);
            string classWhiteSpace = _token.LeadingWhitespace;

            var start = GetStart();
            var name = ReadName();
            var nameExpr = MakeName(name);
            nameExpr.SetLoc(GetStart(), GetEnd());
            string nameWhiteSpace = _token.LeadingWhitespace;
            
            if (name.RealName == null) {
                // no name, assume there's no class.
                return ErrorStmt(_verbatim ? (classWhiteSpace + "class") : null);
            }
            
            bool isParenFree = false;
            string leftParenWhiteSpace = null, rightParenWhiteSpace = null;
            List<string> commaWhiteSpace = null;
            Arg[] args;
            bool ateTerminator = true;
            if (MaybeEat(TokenKind.LeftParenthesis)) {
                leftParenWhiteSpace = _token.LeadingWhitespace;
                commaWhiteSpace = MakeWhiteSpaceList();
                if (_langVersion.Is3x()) {
                    args = FinishArgumentList(null, commaWhiteSpace, out ateTerminator);
                    rightParenWhiteSpace = _token.LeadingWhitespace;
                } else {
                    bool trailingComma;
                    List<Expression> l = ParseTestListAsExpr(null, out commaWhiteSpace, out trailingComma);
                    if (l.Count == 1 && l[0] is ErrorExpression) {
                        // error handling, classes is incomplete.
                        return ErrorStmt(
                            _verbatim ? (classWhiteSpace + "class" + nameWhiteSpace + name.VerbatimName + leftParenWhiteSpace + "(" + ((ErrorExpression)l[0]).VerbatimImage) : null
                        );
                    }
                    args = new Arg[l.Count];
                    for (int i = 0; i < l.Count; i++) {
                        args[i] = new Arg(l[i]);
                    }
                    
                    ateTerminator = Eat(TokenKind.RightParenthesis);
                    rightParenWhiteSpace = _token.LeadingWhitespace;
                }
            } else {
                isParenFree = true;
                args = new Arg[0];
            }
            var mid = GetEnd();

            // Save private prefix
            string savedPrefix = SetPrivatePrefix(name.VerbatimName);

            _classDepth++;
            // Parse the class body
            Statement body = ParseClassOrFuncBody();
            _classDepth--;

            // Restore the private prefix
            _privatePrefix = savedPrefix;

            ClassDefinition ret = new ClassDefinition(nameExpr, args, body);
            AddVerbatimName(name, ret);
            if (_verbatim) {
                if (isParenFree) {
                    AddIsAltForm(ret);
                }
                AddPrecedingWhiteSpace(ret, classWhiteSpace);
                AddSecondPrecedingWhiteSpace(ret, nameWhiteSpace);
                if (leftParenWhiteSpace != null) {
                    AddThirdPrecedingWhiteSpace(ret, leftParenWhiteSpace);
                }
                if (rightParenWhiteSpace != null) {
                    AddFourthPrecedingWhiteSpace(ret, rightParenWhiteSpace);
                }
                if (commaWhiteSpace != null) {
                    AddListWhiteSpace(ret, commaWhiteSpace.ToArray());
                }
                if (!ateTerminator) {
                    AddErrorMissingCloseGrouping(ret);
                }
            }
            ret.HeaderIndex = mid;
            ret.SetLoc(start, body.EndIndex);
            return ret;
        }

        private NameExpression/*!*/ MakeName(Name name) {
            var res = new NameExpression(name.RealName);
            AddVerbatimName(name, res);
            return res;
        }

        private MemberExpression MakeMember(Expression target, Name name) {
            var res = new MemberExpression(target, name.RealName);
            AddVerbatimName(name, res);
            return res;
        }

        //  decorators ::=
        //      decorator+
        //  decorator ::=
        //      "@" dotted_name ["(" [argument_list [","]] ")"] NEWLINE
        private DecoratorStatement ParseDecorators(out List<string> newlineWhiteSpace) {
            List<Expression> decorators = new List<Expression>();
            newlineWhiteSpace = MakeWhiteSpaceList();
            Eat(TokenKind.At);
            var decStart = GetStart();
            do {
                if (newlineWhiteSpace != null) {
                    newlineWhiteSpace.Add(_token.LeadingWhitespace);
                }
                var start = GetStart();
                var name = ReadName();
                Expression decorator = MakeName(name);
                if (!name.HasName) {
                    decorators.Add(null);
                    continue;
                }

                if (_verbatim) {
                    AddPrecedingWhiteSpace(decorator, _token.LeadingWhitespace);
                }
                decorator.SetLoc(GetStart(), GetEnd());
                while (MaybeEat(TokenKind.Dot)) {
                    string whitespace = _token.LeadingWhitespace;
                    name = ReadNameMaybeNone();
                    if (!name.HasName) {
                        var peek = Peek();
                        decorator = Error(
                            _verbatim ?
                                (_token.LeadingWhitespace + _token.Token.VerbatimImage + peek.LeadingWhitespace + peek.Token.VerbatimImage) :
                                null,
                            decorator
                        );
                        Next();
                    } else {
                        string nameWhitespace = _token.LeadingWhitespace;
                        var memberDecorator = MakeMember(decorator, name);
                        memberDecorator.SetLoc(start, GetStart(), GetEnd());
                        if (_verbatim) {
                            AddPrecedingWhiteSpace(memberDecorator, whitespace);
                            AddSecondPrecedingWhiteSpace(memberDecorator, nameWhitespace);
                        }

                        decorator = memberDecorator;
                    }
                }

                if (MaybeEat(TokenKind.LeftParenthesis)) {
                    string parenWhiteSpace = _token.LeadingWhitespace;
                    var commaWhiteSpace = MakeWhiteSpaceList();
                    bool ateTerminator;
                    Arg[] args = FinishArgumentList(null, commaWhiteSpace, out ateTerminator);
                    decorator = FinishCallExpr(decorator, args);

                    if (_verbatim) {
                        AddPrecedingWhiteSpace(decorator, parenWhiteSpace);
                        AddSecondPrecedingWhiteSpace(decorator, _token.LeadingWhitespace);
                        if (commaWhiteSpace != null) {
                            AddListWhiteSpace(decorator, commaWhiteSpace.ToArray());
                        }
                        if (!ateTerminator) {
                            AddErrorMissingCloseGrouping(decorator);
                        }
                    }
                    decorator.SetLoc(start, GetEnd());
                }

                string newline;
                EatNewLine(out newline);
                if (newlineWhiteSpace != null) {
                    newlineWhiteSpace.Add(newline);
                }

                decorators.Add(decorator);
            } while (MaybeEat(TokenKind.At));
             
            var res = new DecoratorStatement(decorators.ToArray());
            res.SetLoc(decStart, GetEnd());
            return res;
        }

        // funcdef: [decorators] 'def' NAME parameters ':' suite
        // 2.6: 
        //  decorated: decorators (classdef | funcdef)
        // this gets called with "@" look-ahead
        private Statement ParseDecorated() {
            List<string> newlineWhiteSpace;
            var decorators = ParseDecorators(out newlineWhiteSpace);

            Statement res;

            var next = PeekToken();
            if (next == Tokens.KeywordDefToken || next == Tokens.KeywordAsyncToken) {
                bool isCoroutine = (next == Tokens.KeywordAsyncToken);
                if (isCoroutine) {
                    Eat(TokenKind.KeywordAsync);
                }
                FunctionDefinition fnc = ParseFuncDef(isCoroutine: isCoroutine);
                fnc.Decorators = decorators;
                fnc.SetLoc(decorators.StartIndex, fnc.EndIndex);
                res = fnc;
            } else if (next == Tokens.KeywordClassToken) {
                if (_langVersion < PythonLanguageVersion.V26) {
                    ReportSyntaxError("invalid syntax, class decorators require 2.6 or later.");
                }
                var cls = ParseClassDef();
                if (cls is ClassDefinition) {
                    ((ClassDefinition)cls).Decorators = decorators;
                    cls.SetLoc(decorators.StartIndex, cls.EndIndex);
                    res = cls;
                } else {
                    // Class was an error...
                    res = ErrorStmt("", decorators, cls);
                }
            } else {
                ReportSyntaxError();
                res = ErrorStmt("", decorators);
            }
            if (newlineWhiteSpace != null) {
                AddNamesWhiteSpace(decorators, newlineWhiteSpace.ToArray());
            }
            return res;
        }

        // funcdef: [decorators] 'def' NAME parameters ':' suite
        // parameters: '(' [varargslist] ')'
        // this gets called with "def" as the look-ahead
        private FunctionDefinition ParseFuncDef(bool isCoroutine) {
            string preWhitespace = null, afterAsyncWhitespace = null;

            var start = isCoroutine ? GetStart() : 0;
            if (isCoroutine) {
                preWhitespace = _token.LeadingWhitespace;
            }
            Eat(TokenKind.KeywordDef);
            
            if (isCoroutine) {
                afterAsyncWhitespace = _token.LeadingWhitespace;
            } else {
                preWhitespace = _token.LeadingWhitespace;
                start = GetStart();
            }

            var name = ReadName();
            var nameExpr = MakeName(name);
            nameExpr.SetLoc(GetStart(), GetEnd());
            string nameWhiteSpace = _token.LeadingWhitespace;

            bool ateLeftParen = name.HasName && Eat(TokenKind.LeftParenthesis);
            string parenWhiteSpace = _token.LeadingWhitespace;

            var lStart = GetStart();
            var lEnd = GetEnd();

            List<string> commaWhiteSpace = null;
            bool ateTerminator = false;
            Parameter[] parameters = ateLeftParen ? ParseVarArgsList(TokenKind.RightParenthesis, out commaWhiteSpace, out ateTerminator, true) : null;
            string closeParenWhiteSpace = _token.LeadingWhitespace;
            FunctionDefinition ret;
            if (parameters == null) {
                // error in parameters
                ret = new FunctionDefinition(nameExpr, new Parameter[0]);
                ret.IsCoroutine = isCoroutine;
                if (_verbatim) {
                    AddVerbatimName(name, ret);
                    AddPrecedingWhiteSpace(ret, preWhitespace);
                    if (afterAsyncWhitespace != null) {
                        GetNodeAttributes(ret)[FunctionDefinition.WhitespaceAfterAsync] = afterAsyncWhitespace;
                    }
                    AddSecondPrecedingWhiteSpace(ret, nameWhiteSpace);
                    AddThirdPrecedingWhiteSpace(ret, parenWhiteSpace);
                    AddFourthPrecedingWhiteSpace(ret, closeParenWhiteSpace);
                    if (!ateTerminator) {
                        AddErrorMissingCloseGrouping(ret);
                    }
                    if (!ateLeftParen) {
                        AddErrorIsIncompleteNode(ret);
                    }
                }
                ret.SetLoc(start, lEnd);
                return ret;
            }

            string arrowWhiteSpace = null;
            Expression returnAnnotation = null;
            if (MaybeEat(TokenKind.Arrow)) {
                arrowWhiteSpace = _token.LeadingWhitespace;
                returnAnnotation = ParseExpression();
            }

            var rStart = GetStart();
            var rEnd = GetEnd();

            ret = new FunctionDefinition(nameExpr, parameters);
            AddVerbatimName(name, ret);

            PushFunction(ret);

            // set IsCoroutine before parsing the body to enable use of 'await'
            ret.IsCoroutine = isCoroutine;

            Statement body = ParseClassOrFuncBody();
            FunctionDefinition ret2 = PopFunction();
            Debug.Assert(ret == ret2);

            ret.SetBody(body);
            ret.ReturnAnnotation = returnAnnotation;
            ret.HeaderIndex = rEnd;
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, preWhitespace);
                if (afterAsyncWhitespace != null) {
                    GetNodeAttributes(ret)[FunctionDefinition.WhitespaceAfterAsync] = afterAsyncWhitespace;
                }
                AddSecondPrecedingWhiteSpace(ret, nameWhiteSpace);
                AddThirdPrecedingWhiteSpace(ret, parenWhiteSpace);
                AddFourthPrecedingWhiteSpace(ret, closeParenWhiteSpace);
                AddListWhiteSpace(ret, commaWhiteSpace.ToArray());
                if (arrowWhiteSpace != null) {
                    AddFifthPrecedingWhiteSpace(ret, arrowWhiteSpace);
                }
                if (!ateTerminator) {
                    AddErrorMissingCloseGrouping(ret);
                }
            }
            ret.SetLoc(start, body.EndIndex);

            return ret;
        }        

        private Parameter ParseParameterName(HashSet<string> names, ParameterKind kind, bool isTyped = false) {
            var start = GetStart();
            var name = ReadName();
            string nameWhiteSpace = _token.LeadingWhitespace;
            if (name.RealName != null) {
                CheckUniqueParameter(start, names, name.RealName);
            } else {
                return null;
            }
            Parameter parameter = new Parameter(name.RealName, kind);
            if (_verbatim) {
                AddSecondPrecedingWhiteSpace(parameter, nameWhiteSpace);
                AddVerbatimName(name, parameter);
            }
            parameter.SetLoc(GetStart(), GetEnd());

            start = GetStart();
            if (isTyped && MaybeEat(TokenKind.Colon)) {
                string colonWhiteSpace = _token.LeadingWhitespace;
                if (_langVersion.Is2x()) {
                    ReportSyntaxError(start, GetEnd(), "invalid syntax, parameter annotations require 3.x");
                }
                parameter.Annotation = ParseExpression();
                if (_verbatim) {
                    AddThirdPrecedingWhiteSpace(parameter, colonWhiteSpace);
                }
            }
            return parameter;
        }

        private void CheckUniqueParameter(int start, HashSet<string> names, string name) {
            if (names.Contains(name)) {
                ReportSyntaxError(start, GetEnd(), String.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "duplicate argument '{0}' in function definition",
                    name));
            }
            names.Add(name);
        }

        //varargslist: (fpdef ['=' expression ] ',')* ('*' NAME [',' '**' NAME] | '**' NAME) | fpdef ['=' expression] (',' fpdef ['=' expression])* [',']
        //fpdef: NAME | '(' fplist ')'
        //fplist: fpdef (',' fpdef)* [',']
        private Parameter[] ParseVarArgsList(TokenKind terminator, out List<string> commaWhiteSpace, out bool ateTerminator, bool isTyped = false) {
            // parameters not doing * or ** today
            List<Parameter> pl = new List<Parameter>();
            commaWhiteSpace = MakeWhiteSpaceList();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            bool needDefault = false, parsedStarArgs = false;
            string namedOnlyText = null;
            for (int position = 0; ; position++) {
                if (MaybeEat(terminator)) {
                    ateTerminator = true;
                    break;
                }

                Parameter parameter;

                var lookahead = Peek();
                if (MaybeEat(TokenKind.Multiply)) {
                    string starWhiteSpace = _token.LeadingWhitespace;
                    if (parsedStarArgs) {
                        ReportSyntaxError(lookahead.Span.Start, GetEnd(), "duplicate * args arguments");
                    }
                    parsedStarArgs = true;

                    if (_langVersion.Is3x()) {
                        if (MaybeEat(TokenKind.Comma)) {
                            string namedOnlyWhiteSpace = _token.LeadingWhitespace;
                            // bare *
                            if (MaybeEat(terminator)) {
                                ReportSyntaxError(lookahead.Span.Start, GetEnd(), "named arguments must follow bare *");
                                ateTerminator = true;
                                break;
                            }
                            if (_verbatim) {
                                namedOnlyText = starWhiteSpace + "*" + namedOnlyWhiteSpace + ",";
                            }
                            continue;
                        }
                    }

                    parameter = ParseParameterName(names, ParameterKind.List, isTyped);
                    if (parameter == null) {
                        // no parameter name, syntax error
                        var peek = Peek();
                        parameter = new ErrorParameter(Error(starWhiteSpace + "*" + peek.LeadingWhitespace + peek.Token.VerbatimImage));
                        Next();
                    } else if (_verbatim) {
                        AddPrecedingWhiteSpace(parameter, starWhiteSpace);
                    }

                    if (namedOnlyText != null) {
                        if (_verbatim) {
                            AddExtraVerbatimText(parameter, namedOnlyText);
                        }
                        namedOnlyText = null;
                    }

                    pl.Add(parameter);

                    if (!MaybeEat(TokenKind.Comma)) {
                        ateTerminator = Eat(terminator);
                        break;
                    }

                    if (commaWhiteSpace != null) {
                        commaWhiteSpace.Add(_token.LeadingWhitespace);
                    }

                    
                    continue;
                } else if (MaybeEat(TokenKind.Power)) {
                    string starStarWhiteSpace = _token.LeadingWhitespace;
                    parameter = ParseParameterName(names, ParameterKind.Dictionary, isTyped);
                    if (parameter == null) {
                        // no parameter name, syntax error
                        var peek = Peek();
                        parameter = new ErrorParameter(Error(starStarWhiteSpace + "**" + peek.LeadingWhitespace + peek.Token.VerbatimImage));
                        Next();
                    }
                    pl.Add(parameter);
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(parameter, starStarWhiteSpace);
                    }
                    ateTerminator = Eat(terminator);

                    if (namedOnlyText != null) {
                        if (_verbatim) {
                            AddExtraVerbatimText(parameter, namedOnlyText);
                        }
                        namedOnlyText = null;
                    }
                    break;
                }

                //
                //  Parsing defparameter:
                //
                //  defparameter ::=
                //      parameter ["=" expression]

                parameter = ParseParameter(position, names, parsedStarArgs ? ParameterKind.KeywordOnly : ParameterKind.Normal, isTyped);
                pl.Add(parameter);
                if (MaybeEat(TokenKind.Assign)) {
                    if (_verbatim) {
                        AddSecondPrecedingWhiteSpace(parameter, _token.LeadingWhitespace);
                    }
                    needDefault = true;
                    parameter.DefaultValue = ParseExpression();
                    parameter.EndIndex = parameter.DefaultValue.EndIndex;
                } else if (needDefault && !parsedStarArgs) {
                    ReportSyntaxError(parameter.StartIndex, parameter.EndIndex, "default value must be specified here");
                }

                if (namedOnlyText != null) {
                    if (_verbatim) {
                        AddExtraVerbatimText(parameter, namedOnlyText);
                    }
                    namedOnlyText = null;
                }

                if (parsedStarArgs && _langVersion.Is2x()) {
                    ReportSyntaxError(parameter.StartIndex, GetEnd(), "positional parameter after * args not allowed");
                }

                if (MaybeEat(TokenKind.Comment)) {
                    // Comment before the comma is a Comment
                    AddTrailingComment(parameter, _token.Token.VerbatimImage);
                }

                if (!MaybeEat(TokenKind.Comma)) {
                    ateTerminator = Eat(terminator);
                    break;
                }

                if (commaWhiteSpace != null) {
                    commaWhiteSpace.Add(_token.LeadingWhitespace);
                }

                if (MaybeEat(TokenKind.Comment)) {
                    // Comment after the comma is whitespace
                    DeferWhitespace(_token.LeadingWhitespace);
                    DeferWhitespace(_token.Token.VerbatimImage);
                }
            }

            return pl.ToArray();
        }

        //  parameter ::=
        //      identifier | "(" sublist ")"
        private Parameter ParseParameter(int position, HashSet<string> names, ParameterKind kind, bool isTyped = false) {
            Name name;
            Parameter parameter;

            if (PeekKind(TokenKind.LeftParenthesis)) {
                // sublist
                var parenWhiteSpace = Next().LeadingWhitespace;
                var parenStart = GetStart();
                Expression ret = ParseSublist(names, true);

                if (_langVersion.Is3x()) {
                    ReportSyntaxError(parenStart, GetEnd(), "sublist parameters are not supported in 3.x");
                }

                bool ateRightParen = Eat(TokenKind.RightParenthesis);
                string closeParenWhiteSpace = _token.LeadingWhitespace;

                TupleExpression tret = ret as TupleExpression;
                NameExpression nameRet;

                if (tret != null) {
                    parameter = new SublistParameter(position, tret);
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(tret, parenWhiteSpace);
                        AddSecondPrecedingWhiteSpace(tret, closeParenWhiteSpace);
                        if (!ateRightParen) {
                            AddErrorMissingCloseGrouping(parameter);
                        }
                    }
                } else if ((nameRet = ret as NameExpression) != null) {
                    parameter = new Parameter(nameRet.Name, kind);
                    if (_verbatim) {
                        AddThirdPrecedingWhiteSpace(parameter, (string)_attributes[nameRet][NodeAttributes.PrecedingWhiteSpace]);
                        AddIsAltForm(parameter);
                        if (!ateRightParen) {
                            AddErrorMissingCloseGrouping(parameter);
                        }
                    }
                } else {
                    Debug.Assert(ret is ErrorExpression);
                    ReportSyntaxError();

                    parameter = new ErrorParameter((ErrorExpression)ret);
                    AddIsAltForm(parameter);
                }

                if (parameter != null) {
                    parameter.SetLoc(ret.IndexSpan);
                }
                if (_verbatim) {
                    AddPrecedingWhiteSpace(parameter, parenWhiteSpace);
                    AddSecondPrecedingWhiteSpace(parameter, closeParenWhiteSpace);
                    if (!ateRightParen) {
                        AddErrorMissingCloseGrouping(parameter);
                    }
                }
            } else if ((name = TokenToName(PeekToken())).HasName) {
                Next();
                var paramStart = GetStart();
                parameter = new Parameter(name.RealName, kind);
                if (_verbatim) {
                    AddPrecedingWhiteSpace(parameter, _token.LeadingWhitespace);
                    AddVerbatimName(name, parameter);
                }
                if (isTyped && MaybeEat(TokenKind.Colon)) {
                    if (_verbatim) {
                        AddThirdPrecedingWhiteSpace(parameter, _token.LeadingWhitespace);
                    }

                    var start = GetStart();
                    parameter.Annotation = ParseExpression();

                    if (_langVersion.Is2x()) {
                        ReportSyntaxError(start, parameter.Annotation.EndIndex, "invalid syntax, parameter annotations require 3.x");
                    }
                }
                CompleteParameterName(parameter, name.RealName, names, paramStart);
            } else {
                ReportSyntaxError();
                Next();
                parameter = new ErrorParameter(_verbatim ? Error(_token.LeadingWhitespace + _token.Token.VerbatimImage) : null);
            }

            return parameter;
        }

        private void CompleteParameterName(Node node, string name, HashSet<string> names, int paramStart) {
            CheckUniqueParameter(paramStart, names, name);
            node.SetLoc(paramStart, GetEnd());
        }

        //  parameter ::=
        //      identifier | "(" sublist ")"
        private Expression ParseSublistParameter(HashSet<string> names) {
            Token t = NextToken();
            Expression ret = null;
            switch (t.Kind) {
                case TokenKind.LeftParenthesis: // sublist
                    string parenWhiteSpace = _token.LeadingWhitespace;
                    ret = ParseSublist(names, false);
                    Eat(TokenKind.RightParenthesis);
                    if (_verbatim && ret is TupleExpression) {
                        AddPrecedingWhiteSpace(ret, parenWhiteSpace);
                        AddSecondPrecedingWhiteSpace(ret, _token.LeadingWhitespace);
                    }
                    break;
                case TokenKind.Name:  // identifier
                    string name = FixName((string)t.Value);
                    NameExpression ne = MakeName(TokenToName(t));
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(ne, _token.LeadingWhitespace);
                    }
                    CompleteParameterName(ne, name, names, GetStart());
                    return ne;
                default:
                    ReportSyntaxError(_token);
                    ret = Error(_verbatim ? (_token.LeadingWhitespace + _token.Token.VerbatimImage) : null);
                    break;
            }
            return ret;
        }

        //  sublist ::=
        //      parameter ("," parameter)* [","]
        private Expression ParseSublist(HashSet<string> names, bool parenFreeTuple) {
            bool trailingComma;
            List<Expression> list = new List<Expression>();
            List<string> itemWhiteSpace = MakeWhiteSpaceList();
            for (; ; ) {
                trailingComma = false;
                list.Add(ParseSublistParameter(names));
                if (MaybeEat(TokenKind.Comma)) {
                    if (itemWhiteSpace != null) {
                        itemWhiteSpace.Add(_token.LeadingWhitespace);
                    }
                    trailingComma = true;
                    switch (PeekKind()) {
                        case TokenKind.LeftParenthesis:
                        case TokenKind.Name:
                            continue;
                        default:
                            break;
                    }
                    break;
                } else {
                    trailingComma = false;
                    break;
                }
            }
            return MakeTupleOrExpr(list, itemWhiteSpace, trailingComma, parenFreeTuple);
        }

        //Python2.5 -> old_lambdef: 'lambda' [varargslist] ':' old_expression
        private Expression FinishOldLambdef() {
            string whitespace = _token.LeadingWhitespace;
            List<string> commaWhiteSpace; 
            bool ateTerminator;
            FunctionDefinition func = ParseLambdaHelperStart(out commaWhiteSpace, out ateTerminator);
            string colonWhiteSpace = _token.LeadingWhitespace;

            Expression expr = ateTerminator ? ParseOldExpression() : Error("");
            return ParseLambdaHelperEnd(func, expr, whitespace, colonWhiteSpace, commaWhiteSpace, ateTerminator);
        }

        //lambdef: 'lambda' [varargslist] ':' expression
        private Expression FinishLambdef() {
            string whitespace = _token.LeadingWhitespace;
            List<string> commaWhiteSpace;
            bool ateTerminator;
            FunctionDefinition func = ParseLambdaHelperStart(out commaWhiteSpace, out ateTerminator);
            string colonWhiteSpace = _token.LeadingWhitespace;

            Expression expr = ateTerminator ? ParseExpression() : Error("");
            return ParseLambdaHelperEnd(func, expr, whitespace, colonWhiteSpace, commaWhiteSpace, ateTerminator);
        }


        // Helpers for parsing lambda expressions. 
        // Usage
        //   FunctionDefinition f = ParseLambdaHelperStart(string);
        //   Expression expr = ParseXYZ();
        //   return ParseLambdaHelperEnd(f, expr);
        private FunctionDefinition ParseLambdaHelperStart(out List<string> commaWhiteSpace, out bool ateTerminator) {
            var start = GetStart();
            Parameter[] parameters;

            parameters = ParseVarArgsList(TokenKind.Colon, out commaWhiteSpace, out ateTerminator);
            var mid = GetEnd();

            FunctionDefinition func = new FunctionDefinition(null, parameters ?? new Parameter[0]); // new Parameter[0] for error handling of incomplete lambda
            func.HeaderIndex = mid;
            func.StartIndex = start;

            // Push the lambda function on the stack so that it's available for any yield expressions to mark it as a generator.
            PushFunction(func);

            return func;
        }

        private Expression ParseLambdaHelperEnd(FunctionDefinition func, Expression expr, string whitespace, string colonWhiteSpace, List<string> commaWhiteSpace, bool ateTerminator) {
            // Pep 342 in Python 2.5 allows Yield Expressions, which can occur inside a Lambda body. 
            // In this case, the lambda is a generator and will yield it's final result instead of just return it.
            Statement body;
            if (func.IsGenerator) {
                YieldExpression y = new YieldExpression(expr);
                y.SetLoc(expr.IndexSpan);
                body = new ExpressionStatement(y);
            } else {
                body = new ReturnStatement(expr);
            }
            body.SetLoc(expr.StartIndex, expr.EndIndex);

            FunctionDefinition func2 = PopFunction();
            System.Diagnostics.Debug.Assert(func == func2);

            func.SetBody(body);
            func.EndIndex = GetEnd();

            LambdaExpression ret = new LambdaExpression(func);
            func.SetLoc(func.IndexSpan);
            ret.SetLoc(func.IndexSpan);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, whitespace);
                AddSecondPrecedingWhiteSpace(ret, colonWhiteSpace);
                AddListWhiteSpace(ret, commaWhiteSpace.ToArray());
                if (!ateTerminator) {
                    AddErrorIsIncompleteNode(ret);
                }
            }
            return ret;
        }

        //while_stmt: 'while' expression ':' suite ['else' ':' suite]
        private WhileStatement ParseWhileStmt() {
            Eat(TokenKind.KeywordWhile);
            string whileWhiteSpace = _token.LeadingWhitespace;
            var start = GetStart();
            Expression expr = ParseExpression();
            var mid = GetEnd();
            Statement body = ParseLoopSuite();
            Statement else_ = null;
            string elseWhiteSpace = null;
            int end = body.EndIndex;
            if (MaybeEat(TokenKind.KeywordElse)) {
                elseWhiteSpace = _token.LeadingWhitespace;
                else_ = ParseSuite();
                end = else_.EndIndex;
            }
            WhileStatement ret = new WhileStatement(expr, body, else_);
            ret.SetLoc(start, mid, end);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, whileWhiteSpace);
                if (elseWhiteSpace != null) {
                    AddSecondPrecedingWhiteSpace(ret, elseWhiteSpace);
                }
            }
            return ret;
        }
        
        //with_stmt: 'with' with_item (',' with_item)* ':' suite
        //with_item: test ['as' expr]
        private WithStatement ParseWithStmt(bool isAsync) {
            var start = isAsync ? GetStart() : 0;
            Eat(TokenKind.KeywordWith);
            if (!isAsync) {
                start = GetStart();
            }

            string withWhiteSpace = _token.LeadingWhitespace;
            var itemWhiteSpace = MakeWhiteSpaceList();

            List<WithItem> items = new List<WithItem>();
            items.Add(ParseWithItem(itemWhiteSpace));
            while (MaybeEat(TokenKind.Comma)) {
                if (itemWhiteSpace != null) {
                    itemWhiteSpace.Add(_token.LeadingWhitespace);
                }
                items.Add(ParseWithItem(itemWhiteSpace));
            }


            var header = GetEnd();
            Statement body = ParseSuite();

            WithStatement ret = new WithStatement(items.ToArray(), body, isAsync);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, withWhiteSpace);
                AddListWhiteSpace(ret, itemWhiteSpace.ToArray());
            }
            ret.SetLoc(start, body.EndIndex);
            return ret;
        }

        private WithItem ParseWithItem(List<string> itemWhiteSpace) {
            var start = GetStart();
            Expression contextManager = ParseExpression();
            Expression var = null;
            if (MaybeEat(TokenKind.KeywordAs)) {
                if (itemWhiteSpace != null) {
                    itemWhiteSpace.Add(_token.LeadingWhitespace);
                }
                var = ParseExpression();
            }

            var res = new WithItem(contextManager, var);
            res.SetLoc(start, GetEnd());
            return res;
        }

        //for_stmt: 'for' target_list 'in' expression_list ':' suite ['else' ':' suite]
        private Statement ParseForStmt(bool isAsync) {
            var start = isAsync ? GetStart() : 0;
            Eat(TokenKind.KeywordFor);
            if (!isAsync) {
                start = GetStart();
            }
            string forWhiteSpace = _token.LeadingWhitespace;

            bool trailingComma;
            List<string> listWhiteSpace;

            List<Expression> l = ParseExpressionList(out trailingComma, out listWhiteSpace);

            // expr list is something like:
            //  ()
            //  a
            //  a,b
            //  a,b,c
            // we either want just () or a or we want (a,b) and (a,b,c)
            // so we can do tupleExpr.EmitSet() or loneExpr.EmitSet()

            string inWhiteSpace = null, elseWhiteSpace = null;
            Expression lhs = MakeTupleOrExpr(l, listWhiteSpace, trailingComma, true);
            Expression list;
            Statement body, else_;
            bool incomplete = false;
            int header;
            string newlineWhiteSpace = "";
            int end;
            if ((lhs is ErrorExpression && MaybeEatNewLine(out newlineWhiteSpace)) || !Eat(TokenKind.KeywordIn)) {                
                // error handling
                else_ = null;
                end = header = GetEnd();
                list = null;
                body = null;
                lhs = Error(newlineWhiteSpace, lhs);
                incomplete = true;                
            } else {
                inWhiteSpace = _token.LeadingWhitespace;
                list = ParseTestListAsExpr();
                header = GetEnd();
                body = ParseLoopSuite();
                else_ = null;
                end = body.EndIndex;
                if (MaybeEat(TokenKind.KeywordElse)) {
                    elseWhiteSpace = _token.LeadingWhitespace;
                    else_ = ParseSuite();
                    end = else_.EndIndex;
                }
            }

            ForStatement ret = new ForStatement(lhs, list, body, else_, isAsync);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, forWhiteSpace);
                if (inWhiteSpace != null) {
                    AddSecondPrecedingWhiteSpace(ret, inWhiteSpace);
                }
                if (elseWhiteSpace != null) {
                    AddThirdPrecedingWhiteSpace(ret, elseWhiteSpace);
                }
                if (incomplete) {
                    AddErrorIsIncompleteNode(ret);
                }
            }
            ret.HeaderIndex = header;
            ret.SetLoc(start, end);
            return ret;
        }

        private Statement ParseLoopSuite() {
            Statement body;
            bool inLoop = _inLoop;
            bool inFinally = _inFinally;
            try {
                _inLoop = true;
                _inFinally = false;
                body = ParseSuite();
            } finally {
                _inLoop = inLoop;
                _inFinally = inFinally;
            }
            return body;
        }

        private Statement ParseClassOrFuncBody() {
            Statement body;
            bool inLoop = _inLoop, inFinally = _inFinally, isGenerator = _isGenerator;
            var returnsWithValue = _returnsWithValue;
            try {
                _inLoop = false;
                _inFinally = false;
                _isGenerator = false;
                _returnsWithValue = null;
                body = ParseSuite();
            } finally {
                _inLoop = inLoop;
                _inFinally = inFinally;
                _isGenerator = isGenerator;
                _returnsWithValue = returnsWithValue;
            }
            return body;
        }

        // if_stmt: 'if' expression ':' suite ('elif' expression ':' suite)* ['else' ':' suite]
        private IfStatement ParseIfStmt() {
            Eat(TokenKind.KeywordIf);
            var itemWhiteSpace = MakeWhiteSpaceList();
            if (itemWhiteSpace != null) {
                itemWhiteSpace.Add(_token.LeadingWhitespace);
            }
            
            var start = GetStart();
            List<IfStatementTest> l = new List<IfStatementTest>();
            l.Add(ParseIfStmtTest());

            while (MaybeEat(TokenKind.KeywordElseIf)) {
                if (itemWhiteSpace != null) {
                    itemWhiteSpace.Add(_token.LeadingWhitespace);
                }
                l.Add(ParseIfStmtTest());
            }

            Statement else_ = null;
            string elseWhiteSpace = null;
            if (MaybeEat(TokenKind.KeywordElse)) {
                elseWhiteSpace = _token.LeadingWhitespace;
                else_ = ParseSuite();
            }

            IfStatementTest[] tests = l.ToArray();
            IfStatement ret = new IfStatement(tests, else_);
            if (_verbatim) {
                if (elseWhiteSpace != null) {
                    AddPrecedingWhiteSpace(ret, elseWhiteSpace);
                }
                if (itemWhiteSpace != null) {
                    AddListWhiteSpace(ret, itemWhiteSpace.ToArray());
                }
            }
            ret.SetLoc(start, else_ != null ? else_.EndIndex : tests[tests.Length - 1].EndIndex);
            return ret;
        }

        private IfStatementTest ParseIfStmtTest() {
            var start = GetStart();
            Expression expr = ParseExpression();
            var header = GetEnd();
            Statement suite = ParseSuite();
            IfStatementTest ret = new IfStatementTest(expr, suite);
            ret.SetLoc(start, suite.EndIndex);
            ret.HeaderIndex = header;
            return ret;
        }

        //try_stmt: ('try' ':' suite (except_clause ':' suite)+
        //    ['else' ':' suite] | 'try' ':' suite 'finally' ':' suite)
        //# NB compile.c makes sure that the default except clause is last

        // Python 2.5 grammar
        //try_stmt: 'try' ':' suite
        //          (
        //            (except_clause ':' suite)+
        //            ['else' ':' suite]
        //            ['finally' ':' suite]
        //          |
        //            'finally' : suite
        //          )


        private Statement ParseTryStatement() {
            Eat(TokenKind.KeywordTry);
            string tryWhiteSpace = _token.LeadingWhitespace;
            var start = GetStart();
            var mid = GetEnd();
            Statement body = ParseSuite();
            Statement finallySuite = null;
            Statement elseSuite = null;
            Statement ret;
            int end;

            string finallyWhiteSpace = null, elseWhiteSpace = null;
            if (MaybeEat(TokenKind.KeywordFinally)) {
                finallyWhiteSpace = _token.LeadingWhitespace;
                finallySuite = ParseFinallySuite(finallySuite);
                end = finallySuite.EndIndex;
                TryStatement tfs = new TryStatement(body, null, elseSuite, finallySuite);
                tfs.HeaderIndex = mid;
                ret = tfs;
            } else {
                List<TryStatementHandler> handlers = new List<TryStatementHandler>();
                TryStatementHandler dh = null;
                end = GetEnd();
                while (true) {
                    if (!MaybeEat(TokenKind.KeywordExcept)) {
                        break;
                    }
                    TryStatementHandler handler = ParseTryStmtHandler();

                    end = handler.EndIndex;
                    handlers.Add(handler);

                    if (dh != null) {
                        ReportSyntaxError(dh.StartIndex, dh.HeaderIndex, "default 'except' must be last");
                    }
                    if (handler.Test == null) {
                        dh = handler;
                    }
                } 

                if (MaybeEat(TokenKind.KeywordElse)) {
                    elseWhiteSpace = _token.LeadingWhitespace;
                    elseSuite = ParseSuite();
                    end = elseSuite.EndIndex;
                }

                if (MaybeEat(TokenKind.KeywordFinally)) {
                    // If this function has an except block, then it can set the current exception.
                    finallyWhiteSpace = _token.LeadingWhitespace;
                    finallySuite = ParseFinallySuite(finallySuite);
                    end = finallySuite.EndIndex;
                }

                TryStatement ts = new TryStatement(body, handlers.ToArray(), elseSuite, finallySuite);
                ts.HeaderIndex = mid;
                ret = ts;
            }
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, tryWhiteSpace);
                if (elseWhiteSpace != null) {
                    AddSecondPrecedingWhiteSpace(ret, elseWhiteSpace);
                }
                if (finallyWhiteSpace != null) {
                    AddThirdPrecedingWhiteSpace(ret, finallyWhiteSpace);
                }
            }
            ret.SetLoc(start, end);
            return ret;
        }

        private Statement ParseFinallySuite(Statement finallySuite) {
            bool inFinally = _inFinally;
            try {
                _inFinally = true;
                finallySuite = ParseSuite();
            } finally {
                _inFinally = inFinally;
            }
            return finallySuite;
        }

        //except_clause: 'except' [expression [',' expression]]
        //2.6: except_clause: 'except' [expression [(',' or 'as') expression]]
        private TryStatementHandler ParseTryStmtHandler() {
            string exceptWhiteSpace = _token.LeadingWhitespace;
            string commaWhiteSpace = null;
            var start = GetStart();
            Expression test1 = null, test2 = null;
            bool altForm = false;
            if (!PeekKind(TokenKind.Colon)) {
                test1 = ParseExpression();

                // parse the expression even if the syntax isn't allowed so we
                // report better error messages when opening against the wrong Python version
                var peek = Peek();
                if (MaybeEat(TokenKind.KeywordAs) || MaybeEatName("as")) {
                    commaWhiteSpace = _token.LeadingWhitespace;
                    if (_langVersion < PythonLanguageVersion.V26) {
                        ReportSyntaxError(peek.Span.Start, peek.Span.End, "'as' requires Python 2.6 or later");
                    }
                    test2 = ParseExpression();
                    altForm = true;
                } else if (MaybeEat(TokenKind.Comma)) {
                    commaWhiteSpace = _token.LeadingWhitespace;
                    test2 = ParseExpression();
                    if (_langVersion.Is3x()) {
                        ReportSyntaxError(peek.Span.Start, GetEnd(), "\", variable\" not allowed in 3.x - use \"as variable\" instead.");
                    }
                }
            }
            var mid = GetEnd();
            Statement body = ParseSuite();
            TryStatementHandler ret = new TryStatementHandler(test1, test2, body);
            ret.HeaderIndex = mid;
            ret.SetLoc(start, body.EndIndex);

            if (_verbatim) {
                if (altForm) {
                    AddIsAltForm(ret);
                }
                if (commaWhiteSpace != null) {
                    AddSecondPrecedingWhiteSpace(ret, commaWhiteSpace);
                }
                AddPrecedingWhiteSpace(ret, exceptWhiteSpace);
            }
            return ret;
        }

        //suite: simple_stmt NEWLINE | Newline INDENT stmt+ DEDENT
        private Statement ParseSuite() {
            if (!Eat(TokenKind.Colon)) {
                // improve error handling...
                var peek = Peek();
                var error = ErrorStmt(_verbatim ? (peek.LeadingWhitespace + peek.Token.VerbatimImage) : null);
                Next();
                return error;
            }

            string colonWhiteSpace = _token.LeadingWhitespace;
            string colonComment = null;

            if (MaybeEat(TokenKind.Comment)) {
                colonComment = _token.Token.VerbatimImage;
            }

            var cur = Peek();
            List<Statement> l = new List<Statement>();

            // we only read a real NewLine here because we need to adjust error reporting
            // for the interpreter.
            SuiteStatement ret;
            if (MaybeEat(TokenKind.NewLine)) {
                string suiteStartWhiteSpace = null;
                if (_verbatim) {
                    suiteStartWhiteSpace = _token.LeadingWhitespace + _token.Token.VerbatimImage;
                }

                CheckSuiteEofError(cur);

                // for error reporting we track the NL tokens and report the error on
                // the last one.  This matches CPython.
                while (MaybeEat(TokenKind.NLToken)) {
                    if (_verbatim) {
                        suiteStartWhiteSpace += _token.LeadingWhitespace + _token.Token.VerbatimImage;
                    }
                }

                if (!MaybeEat(TokenKind.Indent)) {
                    // no indent?  report the indentation error.
                    if (PeekKind(TokenKind.Dedent)) {
                        var peek = Peek();
                        ReportSyntaxError(peek.Span.Start, peek.Span.End, "expected an indented block", ErrorCodes.SyntaxError | ErrorCodes.IncompleteStatement);
                    } else {
                        ReportSyntaxError(cur, ErrorCodes.IndentationError);
                    }
                    return ErrorStmt(_verbatim ? (colonWhiteSpace + ':' + suiteStartWhiteSpace) : null);
                } else if (_verbatim) {
                    // indent white space belongs to the statement we're about to parse
                    DeferWhitespace(suiteStartWhiteSpace + _token.LeadingWhitespace + _token.Token.VerbatimImage);
                }

                while (true) {
                    if (MaybeEat(TokenKind.NLToken)) {
                        if (_verbatim) {
                            DeferWhitespace(_token.LeadingWhitespace);
                        }
                        continue;
                    }
                    if (MaybeEat(TokenKind.Dedent)) {
                        // dedent white space belongs to the statement which follows the suite
                        if (_verbatim) {
                            DeferWhitespace(_token.LeadingWhitespace);
                        }
                        break;
                    }
                    if (PeekKind(TokenKind.EndOfFile)) {
                        ReportSyntaxError("unexpected end of file");
                        break; // error handling
                    }

                    l.Add(ParseStmt());
                }
                ret = new SuiteStatement(l.ToArray());
            } else {
                //  simple_stmt NEWLINE
                //  ParseSimpleStmt takes care of the NEWLINE
                ret = new SuiteStatement(new[] { ParseSimpleStmt() });
                if (_verbatim) {
                    AddSecondPrecedingWhiteSpace(ret, "");
                }
            }

            ret.SetLoc(ret.Statements[0].StartIndex, ret.Statements[ret.Statements.Count - 1].EndIndex);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, colonWhiteSpace);
            }
            if (colonComment != null) {
                // Comment on a suite appears after the colon
                AddTrailingComment(ret, colonComment);
            }
            return ret;
        }

        private void CheckSuiteEofError(TokenWithSpan cur) {
            if (MaybeEatEof()) {
                // for interactive parsing we allow the user to continue in this case
                ReportSyntaxError(PeekToken(), cur.Span, ErrorCodes.SyntaxError, true);
            }
        }

        // Python 2.5 -> old_test: or_test | old_lambdef
        private Expression ParseOldExpression() {
            if (MaybeEat(TokenKind.KeywordLambda)) {
                return FinishOldLambdef();
            }
            return ParseOrTest();
        }

        // expression: conditional_expression | lambda_form
        // conditional_expression: or_test ['if' or_test 'else' expression]
        // lambda_form: "lambda" [parameter_list] : expression
        private Expression ParseExpression() {
            if (MaybeEat(TokenKind.KeywordLambda)) {
                return FinishLambdef();
            }

            Expression ret = ParseOrTest();
            if (ret is ErrorExpression) {
                return ret;
            } else if (MaybeEat(TokenKind.KeywordIf)) {
                var start = ret.StartIndex;
                ret = ParseConditionalTest(ret);
                ret.SetLoc(start, GetEnd());
            }

            return ret;
        }

        // or_test: and_test ('or' and_test)*
        private Expression ParseOrTest() {
            Expression ret = ParseAndTest();
            while (MaybeEat(TokenKind.KeywordOr)) {
                string proceeding = _token.LeadingWhitespace;
                var start = ret.StartIndex;
                ret = new OrExpression(ret, ParseAndTest());
                if (_verbatim) {
                    AddPrecedingWhiteSpace(ret, proceeding);
                }
                ret.SetLoc(start, GetEnd());
            }
            return ret;
        }

        private Expression ParseConditionalTest(Expression trueExpr) {
            string ifWhiteSpace = _token.LeadingWhitespace;
            Expression expr = ParseOrTest();
            bool ateElse = Eat(TokenKind.KeywordElse);
            string elseWhiteSpace;
            Expression falseExpr;
            if (ateElse) {
                elseWhiteSpace = _token.LeadingWhitespace;
                falseExpr = ParseExpression();
            } else {
                elseWhiteSpace = null;
                falseExpr = Error("");
            }
            var res = new ConditionalExpression(expr, trueExpr, falseExpr);
            if (_verbatim) {
                AddPrecedingWhiteSpace(res, ifWhiteSpace);
                AddSecondPrecedingWhiteSpace(res, elseWhiteSpace);
                if (!ateElse) {
                    AddErrorIsIncompleteNode(res);
                }
            }
            return res;
        }

        // and_test: not_test ('and' not_test)*
        private Expression ParseAndTest() {
            Expression ret = ParseNotTest();
            while (MaybeEat(TokenKind.KeywordAnd)) {
                string proceeding = _token.LeadingWhitespace;

                var start = ret.StartIndex;
                ret = new AndExpression(ret, ParseAndTest());
                if (_verbatim) {
                    AddPrecedingWhiteSpace(ret, proceeding);
                }
                ret.SetLoc(start, GetEnd());
            }
            return ret;
        }

        //not_test: 'not' not_test | comparison
        private Expression ParseNotTest() {
            if (MaybeEat(TokenKind.KeywordNot)) {
                string proceeding = _token.LeadingWhitespace;
                var start = GetStart();
                Expression ret = new UnaryExpression(PythonOperator.Not, ParseNotTest());
                if (_verbatim) {
                    AddPrecedingWhiteSpace(ret, proceeding);
                }
                ret.SetLoc(start, GetEnd());
                return ret;
            } else {
                return ParseComparison();
            }
        }
        //comparison: expr (comp_op expr)*
        //comp_op: '<'|'>'|'=='|'>='|'<='|'<>'|'!='|'in'|'not' 'in'|'is'|'is' 'not'
        private Expression ParseComparison() {
            Expression ret = ParseStarExpression();
            while (true) {
                PythonOperator op;
                string whitespaceBeforeOperator = Peek().LeadingWhitespace;
                string secondWhiteSpace = null;
                bool isLessThanGreaterThan = false, isIncomplete = false;                
                switch (PeekKind()) {
                    case TokenKind.LessThan: Next(); op = PythonOperator.LessThan; break;
                    case TokenKind.LessThanOrEqual: Next(); op = PythonOperator.LessThanOrEqual; break;
                    case TokenKind.GreaterThan: Next(); op = PythonOperator.GreaterThan; break;
                    case TokenKind.GreaterThanOrEqual: Next(); op = PythonOperator.GreaterThanOrEqual; break;
                    case TokenKind.Equals: Next(); op = PythonOperator.Equal; break;
                    case TokenKind.NotEquals: Next(); op = PythonOperator.NotEqual; break;
                    case TokenKind.LessThanGreaterThan: Next(); op = PythonOperator.NotEqual; isLessThanGreaterThan = true; break;
                    case TokenKind.KeywordIn: Next(); op = PythonOperator.In; break;

                    case TokenKind.KeywordNot: Next(); isIncomplete = !Eat(TokenKind.KeywordIn); secondWhiteSpace = _token.LeadingWhitespace; op = PythonOperator.NotIn; break;

                    case TokenKind.KeywordIs:
                        Next();
                        if (MaybeEat(TokenKind.KeywordNot)) {
                            op = PythonOperator.IsNot;
                            secondWhiteSpace = _token.LeadingWhitespace;
                        } else {
                            op = PythonOperator.Is;
                        }
                        break;
                    default:
                        return ret;
                }
                Expression rhs = ParseComparison();
                BinaryExpression be = new BinaryExpression(op, ret, rhs);
                if (_verbatim) {
                    AddPrecedingWhiteSpace(be, whitespaceBeforeOperator);
                    GetNodeAttributes(be)[NodeAttributes.SecondPrecedingWhiteSpace] = secondWhiteSpace;
                    if (isLessThanGreaterThan) {
                        AddVerbatimImage(be, "<>");
                    }
                    if (isIncomplete) {
                        AddErrorIsIncompleteNode(be);
                    }
                }
                be.SetLoc(ret.StartIndex, GetEnd());
                ret = be;
            }
        }

        /*
        expr: xor_expr ('|' xor_expr)*
        xor_expr: and_expr ('^' and_expr)*
        and_expr: shift_expr ('&' shift_expr)*
        shift_expr: arith_expr (('<<'|'>>') arith_expr)*
        arith_expr: term (('+'|'-') term)*
        term: factor (('*'|'@'|'/'|'%'|'//') factor)*
        */
        private Expression ParseExpr() {
            return ParseExpr(0);
        }

        private Expression ParseExpr(int precedence) {
            Expression ret = ParseFactor();
            while (true) {
                Token t = PeekToken();
                if (_langVersion >= PythonLanguageVersion.V35 && t.Kind == TokenKind.At) {
                    t = Tokens.MatMultiplyToken;
                }
                OperatorToken ot = t as OperatorToken;
                if (ot == null) return ret;

                int prec = ot.Precedence;
                if (prec >= precedence) {
                    Next();
                    string whiteSpace = _token.LeadingWhitespace;
                    Expression right = ParseExpr(prec + 1);
                    var start = ret.StartIndex;
                    ret = new BinaryExpression(GetBinaryOperator(ot), ret, right);
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(ret, whiteSpace);
                    }
                    ret.SetLoc(start, GetEnd());
                } else {
                    return ret;
                }
            }
        }

        // factor: ('+'|'-'|'~') factor | power
        private Expression ParseFactor() {
            var start = Peek().Span.Start;
            Expression ret;
            switch (PeekKind()) {
                case TokenKind.Add:
                    Next();
                    string posWhiteSpace = _token.LeadingWhitespace;
                    ret = new UnaryExpression(PythonOperator.Pos, ParseFactor());
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(ret, posWhiteSpace);
                    }
                    break;
                case TokenKind.Subtract:
                    Next();
                    ret = FinishUnaryNegate();
                    break;
                case TokenKind.Twiddle:
                    Next();
                    string twiddleWhiteSpace = _token.LeadingWhitespace;
                    ret = new UnaryExpression(PythonOperator.Invert, ParseFactor());
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(ret, twiddleWhiteSpace);
                    }
                    break;
                default:
                    return ParseAwaitExpr();
            }
            ret.SetLoc(start, GetEnd());
            return ret;
        }

        private Expression FinishUnaryNegate() {
            // Special case to ensure that System.Int32.MinValue is an int and not a BigInteger
            if (PeekKind(TokenKind.Constant)) {
                Token t = PeekToken();

                if (t.Value is BigInteger) {
                    BigInteger bi = (BigInteger)t.Value;
                    if (bi == 0x80000000) {
                        string tokenString = t.VerbatimImage;
                        Debug.Assert(tokenString.Length > 0);

                        if (tokenString[tokenString.Length - 1] != 'L' &&
                            tokenString[tokenString.Length - 1] != 'l') {
                            string minusWhiteSpace = _token.LeadingWhitespace;

                            Next();
                            // TODO Fix the white space here
                            var ret = new ConstantExpression(-2147483648);

                            if (_verbatim) {
                                AddExtraVerbatimText(ret, minusWhiteSpace + "-" + _token.LeadingWhitespace + t.VerbatimImage);
                            }
                            return ret;
                        }
                    }
                }
            }

            string whitespace = _token.LeadingWhitespace;
            var res = new UnaryExpression(PythonOperator.Negate, ParseFactor());
            if (_verbatim) {
                AddPrecedingWhiteSpace(res, whitespace);
            }
            return res;
        }

        private Expression ParseAwaitExpr() {
            if (_langVersion >= PythonLanguageVersion.V35) {
                if (AllowAsyncAwaitSyntax && MaybeEat(TokenKind.KeywordAwait)) {
                    var start = GetStart();
                    string whitespace = _token.LeadingWhitespace;
                    var res = new AwaitExpression(ParsePower());
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(res, whitespace);
                    }
                    res.SetLoc(start, GetEnd());
                    return res;
                }
            }
            return ParsePower();
        }

        // power: atom trailer* ['**' factor]
        private Expression ParsePower() {
            Expression ret = ParsePrimary();
            ret = AddTrailers(ret);
            if (MaybeEat(TokenKind.Power)) {
                var start = ret.StartIndex;
                string whitespace = _token.LeadingWhitespace;
                ret = new BinaryExpression(PythonOperator.Power, ret, ParseFactor());
                if (_verbatim) {
                    AddPrecedingWhiteSpace(ret, whitespace);
                }
                ret.SetLoc(start, GetEnd());
            }
            return ret;
        }


        // primary: atom | attributeref | subscription | slicing | call 
        // atom:    identifier | literal | enclosure 
        // enclosure: 
        //      parenth_form | 
        //      list_display | 
        //      generator_expression | 
        //      dict_display | 
        //      string_conversion | 
        //      yield_atom 
        private Expression ParsePrimary() {
            Token t = PeekToken();
            Expression ret;
            switch (t.Kind) {
                case TokenKind.LeftParenthesis: // parenth_form, generator_expression, yield_atom
                    Next();
                    return FinishTupleOrGenExp();
                case TokenKind.LeftBracket:     // list_display
                    Next();
                    return FinishListValue();
                case TokenKind.LeftBrace:       // dict_display
                    Next();
                    return FinishDictOrSetValue();
                case TokenKind.BackQuote:       // string_conversion
                    Next();
                    return FinishStringConversion();
                case TokenKind.KeywordAsync:
                case TokenKind.KeywordAwait:
                // if we made it this far, treat await and async as names
                // See ParseAwaitExpr() for treating 'await' as a keyword
                case TokenKind.Name:            // identifier
                    Next();
                    ret = MakeName(TokenToName(t));
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(ret);
                    }
                    ret.SetLoc(GetStart(), GetEnd());
                    return ret;
                case TokenKind.Ellipsis:
                    Next();
                    ret = new ConstantExpression(Ellipsis.Value);
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(ret, _token.LeadingWhitespace);
                    }
                    ret.SetLoc(GetStart(), GetEnd());
                    return ret;
                case TokenKind.KeywordTrue:
                    Next();
                    ret = new ConstantExpression(true);
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(ret);
                    }
                    ret.SetLoc(GetStart(), GetEnd());
                    return ret;
                case TokenKind.KeywordFalse:
                    Next();
                    ret = new ConstantExpression(false);
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(ret);
                    }
                    ret.SetLoc(GetStart(), GetEnd());
                    return ret;
                case TokenKind.Constant:        // literal
                    Next();
                    var start = GetStart();
                    object cv = t.Value;
                    string cvs = cv as string;
                    AsciiString bytes;
                    if (PeekToken() is ConstantValueToken && (cv is string || cv is AsciiString)) {
                        // string plus
                        string[] verbatimImages = null, verbatimWhiteSpace = null;
                        if (cvs != null) {
                            cv = FinishStringPlus(cvs, t, out verbatimImages, out verbatimWhiteSpace);
                        } else if ((bytes = cv as AsciiString) != null) {
                            cv = FinishBytesPlus(bytes, t, out verbatimImages, out verbatimWhiteSpace);
                        }
                        ret = new ConstantExpression(cv);
                        if (_verbatim) {
                            AddListWhiteSpace(ret, verbatimWhiteSpace);
                            AddVerbatimNames(ret, verbatimImages);
                        }
                    } else {
                        ret = new ConstantExpression(cv);
                        if (_verbatim) {
                            AddExtraVerbatimText(ret, t.VerbatimImage);
                            AddPrecedingWhiteSpace(ret, _token.LeadingWhitespace);
                        }
                    }

                    ret.SetLoc(start, GetEnd());
                    return ret;
                case TokenKind.EndOfFile:
                    // don't eat the end of file token
                    ReportSyntaxError(Peek(), ErrorCodes.SyntaxError, _allowIncomplete);
                    // error node
                    return Error(_verbatim ? "" : null);
                default:
                    ReportSyntaxError(Peek(), ErrorCodes.SyntaxError, _allowIncomplete);
                    if (!PeekKind(TokenKind.NewLine)) {
                        Next();
                        return Error(_verbatim ? (_token.LeadingWhitespace + _token.Token.VerbatimImage) : null);
                    }

                    // error node
                    return Error("");
            }
        }

        private string FinishStringPlus(string s, Token initialToken, out string[] verbatimImages, out string[] verbatimWhiteSpace) {
            List<string> verbatimImagesList = null;
            List<string> verbatimWhiteSpaceList = null;
            if (_verbatim) {
                verbatimWhiteSpaceList = new List<string>();
                verbatimImagesList = new List<string>();
                verbatimWhiteSpaceList.Add(_token.LeadingWhitespace);
                verbatimImagesList.Add(initialToken.VerbatimImage);
            }

            var res = FinishStringPlus(s, verbatimImagesList, verbatimWhiteSpaceList);
            if (_verbatim) {
                verbatimWhiteSpace = verbatimWhiteSpaceList.ToArray();
                verbatimImages = verbatimImagesList.ToArray();
            } else {
                verbatimWhiteSpace = verbatimImages = null;
            }
            return res;
        }

        private string FinishStringPlus(string s, List<string> verbatimImages, List<string> verbatimWhiteSpace) {
            Token t = PeekToken();
            while (true) {
                if (t is ConstantValueToken) {
                    string cvs;
                    AsciiString bytes;
                    if ((cvs = t.Value as String) != null) {
                        s += cvs;
                        Next();
                        if (_verbatim) {
                            verbatimWhiteSpace.Add(_token.LeadingWhitespace);
                            verbatimImages.Add(t.VerbatimImage);
                        }
                        t = PeekToken();
                        continue;
                    } else if ((bytes = t.Value as AsciiString) != null) {
                        if (_langVersion.Is3x()) {
                            ReportSyntaxError("cannot mix bytes and nonbytes literals");
                        }

                        s += bytes.String;
                        Next();
                        if (_verbatim) {
                            verbatimWhiteSpace.Add(_token.LeadingWhitespace);
                            verbatimImages.Add(t.VerbatimImage);
                        }
                        t = PeekToken();
                        continue;
                    } else {
                        ReportSyntaxError("invalid syntax");
                    }
                }
                break;
            }
            return s;
        }

        private object FinishBytesPlus(AsciiString s, Token initialToken, out string[] verbatimImages, out string[] verbatimWhiteSpace) {
            List<string> verbatimImagesList = null;
            List<string> verbatimWhiteSpaceList = null;
            if (_verbatim) {
                verbatimWhiteSpaceList = new List<string>();
                verbatimImagesList = new List<string>();
                verbatimWhiteSpaceList.Add(_token.LeadingWhitespace);
                verbatimImagesList.Add(initialToken.VerbatimImage);
            }

            var res = FinishBytesPlus(s, verbatimImagesList, verbatimWhiteSpaceList);
            
            if (_verbatim) {
                verbatimWhiteSpace = verbatimWhiteSpaceList.ToArray();
                verbatimImages = verbatimImagesList.ToArray();
            } else {
                verbatimWhiteSpace = verbatimImages = null;
            }
            return res;
        }

        private object FinishBytesPlus(AsciiString s, List<string> verbatimImages, List<string> verbatimWhiteSpace) {
            Token t = PeekToken();
            while (true) {
                if (t is ConstantValueToken) {
                    AsciiString cvs;
                    string str;
                    if ((cvs = t.Value as AsciiString) != null) {
                        List<byte> res = new List<byte>(s.Bytes);
                        res.AddRange(cvs.Bytes);
                        s = new AsciiString(res.ToArray(), s.String + cvs.String);
                        Next();
                        if (_verbatim) {
                            verbatimWhiteSpace.Add(_token.LeadingWhitespace);
                            verbatimImages.Add(t.VerbatimImage);
                        }
                        t = PeekToken();
                        continue;
                    } else if ((str = t.Value as string) != null) {
                        if (_langVersion.Is3x()) {
                            ReportSyntaxError("cannot mix bytes and nonbytes literals");
                        }

                        string final = s.String + str;
                        Next();
                        if (_verbatim) {
                            verbatimWhiteSpace.Add(_token.LeadingWhitespace);
                            verbatimImages.Add(t.VerbatimImage);
                        }

                        return FinishStringPlus(final, verbatimImages, verbatimWhiteSpace);
                    } else {
                        ReportSyntaxError("invalid syntax");
                    }
                }
                break;
            }
            return s;
        }

        private Expression AddTrailers(Expression ret) {
            return AddTrailers(ret, true);
        }

        // trailer: '(' [ arglist_genexpr ] ')' | '[' subscriptlist ']' | '.' NAME
        private Expression AddTrailers(Expression ret, bool allowGeneratorExpression) {
            bool prevAllow = _allowIncomplete;
            try {
                _allowIncomplete = true;
                while (true) {
                    string comment = null;
                    if (MaybeEat(TokenKind.Comment)) {
                        comment = _token.Token.VerbatimImage;
                    }

                    switch (PeekKind()) {
                        case TokenKind.LeftParenthesis:
                            if (!allowGeneratorExpression) return ret;

                            Next();
                            string whitespace = _token.LeadingWhitespace;
                            List<string> commaWhiteSpace;
                            bool ateTerminator;
                            Arg[] args = FinishArgListOrGenExpr(out commaWhiteSpace, out ateTerminator);
                            string closeParenWhiteSpace = _token.LeadingWhitespace;
                            CallExpression call;
                            if (args != null) {
                                call = FinishCallExpr(ret, args);
                            } else {
                                call = new CallExpression(ret, new Arg[0]);
                            }

                            if (_verbatim) {
                                AddPrecedingWhiteSpace(call, whitespace);
                                AddSecondPrecedingWhiteSpace(call, closeParenWhiteSpace);
                                if (commaWhiteSpace != null) {
                                    AddListWhiteSpace(call, commaWhiteSpace.ToArray());
                                }
                                if (!ateTerminator) {
                                    AddErrorMissingCloseGrouping(call);
                                }
                            }

                            call.SetLoc(ret.StartIndex, GetEnd());
                            ret = call;
                            break;
                        case TokenKind.LeftBracket:
                            Next();
                            whitespace = _token.LeadingWhitespace;

                            Expression index = ParseSubscriptList(out ateTerminator);
                            IndexExpression ie = new IndexExpression(ret, index);
                            string finishWhiteSpace = _token.LeadingWhitespace;
                            ie.SetLoc(ret.StartIndex, GetEnd());
                            if (_verbatim) {
                                AddPrecedingWhiteSpace(ie, whitespace);
                                AddSecondPrecedingWhiteSpace(ie, finishWhiteSpace);
                                if (!ateTerminator) {
                                    AddErrorMissingCloseGrouping(ie);
                                }
                            }
                            ret = ie;
                            break;
                        case TokenKind.Dot:
                            Next();
                            whitespace = _token.LeadingWhitespace;
                            var name = ReadNameMaybeNone();
                            string nameWhitespace = _token.LeadingWhitespace;
                            MemberExpression fe = MakeMember(ret, name);
                            fe.SetLoc(ret.StartIndex, GetStart(), GetEnd());
                            if (_verbatim) {
                                AddPrecedingWhiteSpace(fe, whitespace);
                                AddSecondPrecedingWhiteSpace(fe, nameWhitespace);
                                if (!name.HasName) {
                                    AddErrorIsIncompleteNode(fe);
                                }
                            }
                            ret = fe;
                            break;
                        case TokenKind.Constant:
                            // abc.1, abc"", abc 1L, abc 0j
                            ReportSyntaxError("invalid syntax");
                            var peek = Peek();
                            ret = Error(_verbatim ? peek.LeadingWhitespace + peek.Token.VerbatimImage : null, ret);
                            Next();
                            break;
                        default:
                            if (!string.IsNullOrEmpty(comment)) {
                                AddTrailingComment(ret, comment);
                            }
                            return ret;
                    }
                }
            } finally {
                _allowIncomplete = prevAllow;
            }
        }

        //subscriptlist: subscript (',' subscript)* [',']
        //subscript: '.' '.' '.' | expression | [expression] ':' [expression] [sliceop]
        //sliceop: ':' [expression]
        private Expression ParseSubscriptList(out bool ateTerminator) {
            const TokenKind terminator = TokenKind.RightBracket;
            var start0 = GetStart();
            bool trailingComma = false;

            List<Expression> l = new List<Expression>();
            List<string> listWhiteSpace = MakeWhiteSpaceList();
            while (true) {
                Expression e;
                if (MaybeEat(TokenKind.Dot)) {
                    string whitespace = _token.LeadingWhitespace;
                    var start = GetStart();
                    if (Eat(TokenKind.Dot)) {
                        if (Eat(TokenKind.Dot)) {
                            e = new ConstantExpression(Ellipsis.Value);
                            if (_verbatim) {
                                AddPrecedingWhiteSpace(e, whitespace);
                            }
                        } else {
                            e = Error(_verbatim ? whitespace + ".." : null);
                        }
                    } else {
                        e = Error(_verbatim ? whitespace + "." : null);
                    }
                    e.SetLoc(start, GetEnd());
                } else if (MaybeEat(TokenKind.Colon)) {
                    e = FinishSlice(null, GetStart());
                } else {
                    e = ParseExpression();
                    if (MaybeEat(TokenKind.Colon)) {
                        e = FinishSlice(e, e.StartIndex);
                    }
                }

                l.Add(e);
                if (!MaybeEat(TokenKind.Comma)) {
                    ateTerminator = Eat(terminator);
                    trailingComma = false;
                    break;
                }
                if (listWhiteSpace != null) {
                    listWhiteSpace.Add(_token.LeadingWhitespace);
                }

                trailingComma = true;
                if (MaybeEat(terminator)) {
                    ateTerminator = true;
                    break;
                }
            }
            Expression ret = MakeTupleOrExpr(l, listWhiteSpace, trailingComma, true);
            if (l.Count != 1 || ret != l[0]) {
                ret.SetLoc(start0, GetEnd());
            }
            return ret;
        }

        private Expression ParseSliceEnd() {
            Expression e2 = null;
            switch (PeekKind()) {
                case TokenKind.Comma:
                case TokenKind.RightBracket:
                    break;
                default:
                    e2 = ParseExpression();
                    break;
            }
            return e2;
        }

        private Expression FinishSlice(Expression e0, int start) {
            Expression e1 = null;
            Expression e2 = null;
            bool stepProvided = false;
            Debug.Assert(_token.Token.Kind == TokenKind.Colon);
            string colonWhiteSpace = _token.LeadingWhitespace;
            string secondColonWhiteSpace = null;

            switch (PeekKind()) {
                case TokenKind.Comma:
                case TokenKind.RightBracket:
                    break;
                case TokenKind.Colon:
                    // x[?::?]
                    stepProvided = true;
                    Next();
                    secondColonWhiteSpace = _token.LeadingWhitespace;
                    e2 = ParseSliceEnd();
                    break;
                default:
                    // x[?:val:?]
                    e1 = ParseExpression();
                    if (MaybeEat(TokenKind.Colon)) {
                        secondColonWhiteSpace = _token.LeadingWhitespace;
                        stepProvided = true;
                        e2 = ParseSliceEnd();
                    }
                    break;
            }
            SliceExpression ret = new SliceExpression(e0, e1, e2, stepProvided);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, colonWhiteSpace);
                if (secondColonWhiteSpace != null) {
                    AddSecondPrecedingWhiteSpace(ret, secondColonWhiteSpace);
                }
            }
            ret.SetLoc(start, GetEnd());
            return ret;
        }


        //exprlist: expr (',' expr)* [',']
        private List<Expression> ParseExprList(out List<string> commaWhiteSpace) {
            List<Expression> l = new List<Expression>();
            commaWhiteSpace = MakeWhiteSpaceList();
            while (true) {
                Expression e = ParseExpr();
                l.Add(e);
                if (!MaybeEat(TokenKind.Comma)) {
                    break;
                }
                if (commaWhiteSpace != null) {
                    commaWhiteSpace.Add(_token.LeadingWhitespace);
                }
                if (NeverTestToken(PeekToken())) {
                    break;
                }
            }
            return l;
        }

        // arglist:
        //             expression                     rest_of_arguments
        //             expression "=" expression      rest_of_arguments
        //             expression "for" gen_expr_rest
        //
        private Arg[] FinishArgListOrGenExpr(out List<string> commaWhiteSpace, out bool ateTerminator) {
            Arg a = null;
            commaWhiteSpace = MakeWhiteSpaceList();

            Token t = PeekToken();
            if (t.Kind != TokenKind.RightParenthesis && t.Kind != TokenKind.Multiply && t.Kind != TokenKind.Power) {
                Expression e = ParseExpression();
                if (e is ErrorExpression) {
                    ateTerminator = false;
                    return new[] { new Arg(e) };
                }

                if (MaybeEat(TokenKind.Assign)) {               //  Keyword argument
                    a = FinishKeywordArgument(e);
                } else if (PeekToken(Tokens.KeywordForToken)) {    //  Generator expression
                    var genExpr = ParseGeneratorExpression(e);
                    AddIsAltForm(genExpr);
                    a = new Arg(genExpr);
                    ateTerminator = Eat(TokenKind.RightParenthesis);
                    a.SetLoc(e.StartIndex, GetEnd());
                    return new Arg[1] { a };       //  Generator expression is the argument
                } else {
                    a = new Arg(e);
                    a.SetLoc(e.StartIndex, e.EndIndex);
                }

                //  Is there a comment?
                //
                if (MaybeEat(TokenKind.Comment)) {
                    DeferWhitespace(_token.Token.Image);
                }

                //  Was this all?
                //
                if (MaybeEat(TokenKind.Comma)) {
                    if (commaWhiteSpace != null) {
                        commaWhiteSpace.Add(_token.LeadingWhitespace);
                    }
                } else {
                    ateTerminator = Eat(TokenKind.RightParenthesis);
                    a.SetLoc(e.StartIndex, GetEnd());
                    return new Arg[1] { a };
                }
            }

            return FinishArgumentList(a, commaWhiteSpace, out ateTerminator);   // TODO: Use ateTerminator
        }

        private Arg FinishKeywordArgument(Expression t) {
            Debug.Assert(_token.Token.Kind == TokenKind.Assign);
            string equalWhiteSpace = _token.LeadingWhitespace;
            NameExpression n = t as NameExpression;
            
            string name;
            if (n == null) {
                ReportSyntaxError(t.StartIndex, t.EndIndex, "expected name");
                name = null;
            } else {
                name = n.Name;
            }

            Expression val = ParseExpression();
            Arg arg = new Arg(t, val);
            arg.SetLoc(t.StartIndex, val.EndIndex);
            if (_verbatim) {
                AddPrecedingWhiteSpace(arg, equalWhiteSpace);
            }

            // we're losing the name expression...
            return arg;
        }

        private void CheckUniqueArgument(List<Arg> names, Arg arg) {
            if (arg != null && arg.Name != null) {
                string name = arg.Name;
                for (int i = 0; i < names.Count; i++) {
                    if (names[i].Name == arg.Name) {
                        ReportSyntaxError("duplicate keyword argument");
                    }
                }
            }
        }

        //arglist: (argument ',')* (argument [',']| '*' expression [',' '**' expression] | '**' expression)
        //argument: [expression '='] expression    # Really [keyword '='] expression
        private Arg[] FinishArgumentList(Arg first, List<string> commaWhiteSpace, out bool ateTerminator) {
            const TokenKind terminator = TokenKind.RightParenthesis;
            List<Arg> l = new List<Arg>();

            if (first != null) {
                l.Add(first);
            }

            // Parse remaining arguments
            while (true) {
                if (MaybeEat(terminator)) {
                    ateTerminator = true;
                    break;
                }
                int start;
                Arg a;
                if (MaybeEat(TokenKind.Multiply)) {
                    string starWhiteSpace = _token.LeadingWhitespace;
                    start = GetStart();
                    Expression t = ParseExpression();
                    var name = new NameExpression("*");
                    a = new Arg(name, t);
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(name, starWhiteSpace);
                    }
                } else if (MaybeEat(TokenKind.Power)) {
                    string starStarWhiteSpace = _token.LeadingWhitespace;
                    start = GetStart();
                    Expression t = ParseExpression();
                    var name = new NameExpression("**");
                    a = new Arg(name, t);
                    if (_verbatim) {
                        AddPrecedingWhiteSpace(name, starStarWhiteSpace);
                    }
                } else {
                    Expression e = ParseExpression();
                    start = e.StartIndex;
                    if (MaybeEat(TokenKind.Assign)) {
                        a = FinishKeywordArgument(e);
                        CheckUniqueArgument(l, a);
                    } else {
                        a = new Arg(e);
                    }
                }
                a.SetLoc(start, GetEnd());
                l.Add(a);
                if (MaybeEat(TokenKind.Comma)) {
                    if (commaWhiteSpace != null) {
                        commaWhiteSpace.Add(_token.LeadingWhitespace);
                    }
                } else {
                    ateTerminator = Eat(terminator);
                    break;
                }
            }

            return l.ToArray();
        }

        private Expression ParseOldExpressionListAsExpr() {
            bool trailingComma;
            List<string> itemWhiteSpace;
            List<Expression> l = ParseOldExpressionList(out trailingComma, out itemWhiteSpace);
            //  the case when no expression was parsed e.g. when we have an empty expression list
            if (l.Count == 0 && !trailingComma) {
                ReportSyntaxError("invalid syntax");
            }
            return MakeTupleOrExpr(l, itemWhiteSpace, trailingComma, true);
        }

        // old_expression_list: old_expression [(',' old_expression)+ [',']]
        private List<Expression> ParseOldExpressionList(out bool trailingComma, out List<string> itemWhiteSpace) {
            List<Expression> l = new List<Expression>();
            itemWhiteSpace = MakeWhiteSpaceList();
            trailingComma = false;
            while (true) {
                if (NeverTestToken(PeekToken())) break;
                l.Add(ParseOldExpression());
                if (!MaybeEat(TokenKind.Comma)) {
                    trailingComma = false;
                    break;
                }
                if (itemWhiteSpace != null) {
                    itemWhiteSpace.Add(_token.LeadingWhitespace);
                }
                trailingComma = true;
            }
            return l;
        }

        // expression_list: expression (',' expression)* [',']
        private List<Expression> ParseExpressionList(out bool trailingComma, out List<string> whitespace) {
            List<Expression> l = new List<Expression>();
            trailingComma = false;
            whitespace = MakeWhiteSpaceList();

            while (true) {
                if (NeverTestToken(PeekToken())) break;
                l.Add(ParseStarExpression());
                if (!MaybeEat(TokenKind.Comma)) {
                    trailingComma = false;
                    break;
                }
                if (whitespace != null) {
                    whitespace.Add(_token.LeadingWhitespace);
                }
                trailingComma = true;
            }

            return l;
        }

        // 3.x: star_expr: ['*'] expr
        private Expression ParseStarExpression() {
            
            if (MaybeEat(TokenKind.Multiply)) {
                string whitespace = _token.LeadingWhitespace;
                if (_langVersion.Is2x()) {
                    ReportSyntaxError("invalid syntax");
                }
                var start = GetStart();
                var expr = ParseExpr();
                var res = new StarredExpression(expr);
                if (_verbatim) {
                    AddPrecedingWhiteSpace(res, whitespace);
                }
                res.SetLoc(start, expr.EndIndex);
                return res;
            }

            return ParseExpr();
        }

        private Expression ParseTestListAsExpr() {
            if (!NeverTestToken(PeekToken())) {
                var expr = ParseExpression();
                if (!MaybeEat(TokenKind.Comma)) {
                    return expr;
                }

                return ParseTestListAsExpr(expr);
            } else {
                return ParseTestListAsExprError();
            }
        }

        private Expression ParseTestListAsExpr(Expression expr) {
            
            List<string> itemWhiteSpace;
            bool trailingComma;
            List<Expression> l = ParseTestListAsExpr(expr, out itemWhiteSpace, out trailingComma);
            return MakeTupleOrExpr(l, itemWhiteSpace, trailingComma, parenFreeTuple: true);
        }

        private List<Expression> ParseTestListAsExpr(Expression expr, out List<string> itemWhiteSpace, out bool trailingComma) {
            var l = new List<Expression>();
            itemWhiteSpace = MakeWhiteSpaceList();
            if (expr != null) {
                l.Add(expr);
                if (itemWhiteSpace != null) {
                    Debug.Assert(_token.Token.Kind == TokenKind.Comma);
                    itemWhiteSpace.Add(_token.LeadingWhitespace);
                }
            }

            trailingComma = true;
            while (true) {
                if (NeverTestToken(PeekToken())) break;
                l.Add(ParseExpression());

                if (!MaybeEat(TokenKind.Comma)) {
                    trailingComma = false;
                    break;
                }
                if (itemWhiteSpace != null) {
                    itemWhiteSpace.Add(_token.LeadingWhitespace);
                }
            }
            return l;
        }

        private Expression ParseTestListAsExprError() {
            if (MaybeEat(TokenKind.Indent)) {
                // the error is on the next token which has a useful location, unlike the indent - note we don't have an
                // indent if we're at an EOF.  It'a also an indentation error instead of a syntax error.
                string indentVerbatim = _verbatim ? _token.LeadingWhitespace + _token.Token.VerbatimImage : null;
                Next();
                ReportSyntaxError(GetStart(), GetEnd(), "unexpected indent", ErrorCodes.IndentationError);
                return Error(_verbatim ? (indentVerbatim + _token.LeadingWhitespace + _token.Token.VerbatimImage) : null);
            } else {
                ReportSyntaxError();
            }
            Next();
            return Error(_verbatim ? (_token.LeadingWhitespace + _token.Token.VerbatimImage) : null);
        }

        private Expression FinishExpressionListAsExpr(Expression expr) {
            var start = GetStart();
            bool trailingComma = true;
            List<Expression> l = new List<Expression>();
            List<string> itemWhiteSpace = MakeWhiteSpaceList();
            if (itemWhiteSpace != null) {
                itemWhiteSpace.Add(_token.LeadingWhitespace);
            }
            l.Add(expr);

            while (true) {
                if (NeverTestToken(PeekToken())) break;
                expr = ParseExpression();
                l.Add(expr);
                if (!MaybeEat(TokenKind.Comma)) {
                    trailingComma = false;
                    break;
                }
                if (itemWhiteSpace != null) {
                    itemWhiteSpace.Add(_token.LeadingWhitespace);
                }
                trailingComma = true;
            }

            Expression ret = MakeTupleOrExpr(l, itemWhiteSpace, trailingComma);
            ret.SetLoc(start, GetEnd());
            return ret;
        }

        //
        //  testlist_gexp: expression ( genexpr_for | (',' expression)* [','] )
        //
        private Expression FinishTupleOrGenExp() {
            string startingWhiteSpace = _token.LeadingWhitespace;
            var lStart = GetStart();
            var lEnd = GetEnd();
            bool hasRightParenthesis;

            Expression ret;
            //  Empty tuple
            if (MaybeEat(TokenKind.RightParenthesis)) {
                ret = MakeTupleOrExpr(new List<Expression>(), MakeWhiteSpaceList(), false);
                hasRightParenthesis = true;
            } else if (PeekKind(TokenKind.KeywordYield)) {
                if (!AllowYieldSyntax && AllowAsyncAwaitSyntax) {
                    ReportSyntaxError("'yield' inside async function");
                }
                Eat(TokenKind.KeywordYield);
                ret = ParseYieldExpression();
                Eat(TokenKind.RightParenthesis);                
                hasRightParenthesis = true;
            } else {
                bool prevAllow = _allowIncomplete;
                try {
                    _allowIncomplete = true;

                    Expression expr = ParseExpression();
                    if (MaybeEat(TokenKind.Comma)) {
                        // "(" expression "," ...
                        ret = FinishExpressionListAsExpr(expr);
                    } else if (PeekToken(Tokens.KeywordForToken)) {
                        // "(" expression "for" ...
                        ret = ParseGeneratorExpression(expr, startingWhiteSpace);                        
                    } else {
                        // "(" expression ")"
                        ret = new ParenthesisExpression(expr);
                    }
                    hasRightParenthesis = Eat(TokenKind.RightParenthesis);
                } finally {
                    _allowIncomplete = prevAllow;
                }
            }
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, startingWhiteSpace);
                AddSecondPrecedingWhiteSpace(ret, _token.LeadingWhitespace);
                if (!hasRightParenthesis) {
                    AddErrorMissingCloseGrouping(ret);
                }
            }
            var rStart = GetStart();
            var rEnd = GetEnd();

            ret.SetLoc(lStart, rEnd);
            return ret;
        }

        //  genexpr_for  ::= "for" target_list "in" or_test [comp_iter]
        //
        //  "for" has NOT been eaten before entering this method
        private Expression ParseGeneratorExpression(Expression expr, string rightParenWhiteSpace = null) {
            ComprehensionIterator[] iters = ParseCompIter();

            GeneratorExpression ret = new GeneratorExpression(expr, iters);

            ret.SetLoc(expr.StartIndex, GetEnd());
            return ret;
        }

        private static Statement NestGenExpr(Statement current, Statement nested) {
            ForStatement fes = current as ForStatement;
            IfStatement ifs;
            if (fes != null) {
                fes.Body = nested;
            } else if ((ifs = current as IfStatement) != null) {
                ifs.Tests[0].Body = nested;
            }
            return nested;
        }

        /*
        // "for" target_list "in" or_test
        private ForStatement ParseGenExprFor() {
            var start = GetStart();
            Eat(TokenKind.KeywordFor);
            bool trailingComma;
            List<string> listWhiteSpace;
            List<Expression> l = ParseTargetList(out trailingComma, out listWhiteSpace);
            Expression lhs = MakeTupleOrExpr(l, listWhiteSpace, trailingComma);
            Eat(TokenKind.KeywordIn);

            Expression expr = null;
            expr = ParseOrTest();

            ForStatement gef = new ForStatement(lhs, expr, null, null);
            var end = GetEnd();
            gef.SetLoc(start, end);
            gef.HeaderIndex = end;
            return gef;
        }

        //  genexpr_if: "if" old_test
        private IfStatement ParseGenExprIf() {
            var start = GetStart();
            Eat(TokenKind.KeywordIf);
            Expression expr = ParseOldExpression();
            IfStatementTest ist = new IfStatementTest(expr, null);
            var end = GetEnd();
            ist.HeaderIndex = end;
            ist.SetLoc(start, end);
            IfStatement gei = new IfStatement(new IfStatementTest[] { ist }, null);
            gei.SetLoc(start, end);
            return gei;
        }
        */

        // dict_display: '{' [dictorsetmaker] '}'
        // dictorsetmaker: ( (test ':' test (comp_for | (',' test ':' test)* [','])) |
        //                   (test (comp_for | (',' test)* [','])) )


        private Expression FinishDictOrSetValue() {
            string startWhiteSpace = _token.LeadingWhitespace, finishWhiteSpace;
            var oStart = GetStart();
            var oEnd = GetEnd();

            List<SliceExpression> dictMembers = null;
            List<Expression> setMembers = null;
            List<string> itemWhiteSpace = MakeWhiteSpaceList();
            bool prevAllow = _allowIncomplete;
            bool reportedError = false;
            bool ateTerminator = false;
            try {
                _allowIncomplete = true;
                while (true) {
                    if (MaybeEat(TokenKind.RightBrace)) { // empty dict literal
                        finishWhiteSpace = _token.LeadingWhitespace;
                        ateTerminator = true;
                        break;
                    }
                    bool first = false;
                    Expression e1 = ParseExpression();
                    if (MaybeEat(TokenKind.Colon)) { // dict literal
                        string colonWhiteSpace = _token.LeadingWhitespace;
                        if (setMembers == null && dictMembers == null) {
                            dictMembers = new List<SliceExpression>();
                            first = true;
                        }
                        Expression e2 = ParseExpression();

                        if (setMembers != null) {
                            if (!reportedError) {
                                ReportSyntaxError(e1.StartIndex, e2.EndIndex, "invalid syntax");
                            }
                        }


                        SliceExpression se = new SliceExpression(e1, e2, null, false);
                        if (_verbatim) {
                            AddPrecedingWhiteSpace(se, colonWhiteSpace);
                        }
                        se.SetLoc(e1.StartIndex, e2.EndIndex);

                        if (PeekToken(Tokens.KeywordForToken)) {
                            if (!first || _langVersion < PythonLanguageVersion.V27) {
                                ReportSyntaxError("invalid syntax");
                            }

                            var dictComp = FinishDictComp(se, out ateTerminator);
                            if (_verbatim) {
                                AddPrecedingWhiteSpace(dictComp, startWhiteSpace);
                                AddSecondPrecedingWhiteSpace(dictComp, _token.LeadingWhitespace);
                                if (!ateTerminator) {
                                    AddErrorMissingCloseGrouping(dictComp);
                                }
                            }
                            dictComp.SetLoc(oStart, GetEnd());
                            return dictComp;
                        }

                        if (dictMembers != null) {
                            dictMembers.Add(se);
                        } else {
                            setMembers.Add(se);
                        }
                    } else { // set literal
                        if (_langVersion < PythonLanguageVersion.V27 && !reportedError) {
                            ReportSyntaxError(e1.StartIndex, e1.EndIndex, "invalid syntax, set literals require Python 2.7 or later.");
                            reportedError = true;
                        }
                        if (dictMembers != null) {
                            if (!reportedError) {
                                ReportSyntaxError(e1.StartIndex, e1.EndIndex, "invalid syntax");
                            }
                        } else if (setMembers == null) {
                            setMembers = new List<Expression>();
                            first = true;
                        }

                        if (PeekToken(Tokens.KeywordForToken)) {
                            if (!first) {
                                ReportSyntaxError("invalid syntax");
                            }
                            var setComp = FinishSetComp(e1, out ateTerminator);
                            if (_verbatim) {
                                AddPrecedingWhiteSpace(setComp, startWhiteSpace);
                                AddSecondPrecedingWhiteSpace(setComp, _token.LeadingWhitespace);
                                if (!ateTerminator) {
                                    AddErrorMissingCloseGrouping(setComp);
                                }
                            }
                            setComp.SetLoc(oStart, GetEnd());
                            return setComp;
                        }

                        // error recovery
                        if (setMembers != null) {
                            setMembers.Add(e1);
                        } else {
                            var slice = new SliceExpression(e1, null, null, false);
                            if (_verbatim) {
                                AddErrorIsIncompleteNode(slice);
                            }
                            dictMembers.Add(slice);
                        }
                    }

                    if (!MaybeEat(TokenKind.Comma)) {
                        ateTerminator = Eat(TokenKind.RightBrace);
                        finishWhiteSpace = _token.LeadingWhitespace;
                        break;
                    }
                    if (itemWhiteSpace != null) {
                        itemWhiteSpace.Add(_token.LeadingWhitespace);
                    }
                }
            } finally {
                _allowIncomplete = prevAllow;
            }


            var cStart = GetStart();
            var cEnd = GetEnd();

            Expression ret;
            if (dictMembers != null || setMembers == null) {
                SliceExpression[] exprs;
                if (dictMembers != null) {
                    exprs = dictMembers.ToArray();
                } else {
                    exprs = new SliceExpression[0];
                }
                ret = new DictionaryExpression(exprs);
            } else {
                ret = new SetExpression(setMembers.ToArray());
            }
            ret.SetLoc(oStart, cEnd);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, startWhiteSpace);
                AddSecondPrecedingWhiteSpace(ret, finishWhiteSpace);
                AddListWhiteSpace(ret, itemWhiteSpace.ToArray());
                if (!ateTerminator) {
                    AddErrorMissingCloseGrouping(ret);
                }
            }
            return ret;
        }

        // comp_iter '}'
        private SetComprehension FinishSetComp(Expression item, out bool ateTerminator) {
            ComprehensionIterator[] iters = ParseCompIter();
            ateTerminator = Eat(TokenKind.RightBrace);
            return new SetComprehension(item, iters);
        }

        // comp_iter '}'
        private DictionaryComprehension FinishDictComp(SliceExpression value, out bool ateTerminator) {
            ComprehensionIterator[] iters = ParseCompIter();
            ateTerminator = Eat(TokenKind.RightBrace);
            return new DictionaryComprehension(value, iters);
        }

        // comp_iter: comp_for | comp_if
        private ComprehensionIterator[] ParseCompIter() {
            List<ComprehensionIterator> iters = new List<ComprehensionIterator>();
            ComprehensionFor firstFor = ParseCompFor();
            iters.Add(firstFor);

            while (true) {
                if (PeekToken(Tokens.KeywordForToken)) {
                    iters.Add(ParseCompFor());
                } else if (PeekToken(Tokens.KeywordIfToken)) {
                    iters.Add(ParseCompIf());
                } else {
                    break;
                }
            }

            return iters.ToArray();
        }

        // comp_for: 'for target_list 'in' or_test [comp_iter]
        private ComprehensionFor ParseCompFor() {
            Eat(TokenKind.KeywordFor);
            string forWhiteSpace = _token.LeadingWhitespace;

            var start = GetStart();
            bool trailingComma;
            List<string> listWhiteSpace;
            List<Expression> l = ParseExpressionList(out trailingComma, out listWhiteSpace);

            // expr list is something like:
            //  ()
            // a
            // a,b
            // a,b,c
            // we either want just () or a or we want (a,b) and (a,b,c)
            // so we can do tupleExpr.EmitSet() or loneExpr.EmitSet()

            Expression lhs = MakeTupleOrExpr(l, listWhiteSpace, trailingComma, true);
            bool ateIn = Eat(TokenKind.KeywordIn);
            
            string inWhiteSpace;
            Expression list;
            if (ateIn) {
                inWhiteSpace = _token.LeadingWhitespace;
                list = ParseOrTest();
            } else {
                inWhiteSpace = null;
                list = Error("");
            }

            ComprehensionFor ret = new ComprehensionFor(lhs, list);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, forWhiteSpace);
                AddSecondPrecedingWhiteSpace(ret, inWhiteSpace);
                if (!ateIn) {
                    AddErrorIsIncompleteNode(ret);
                }
            }

            ret.SetLoc(start, GetEnd());
            return ret;
        }

        // listmaker: expression ( list_for | (',' expression)* [','] )
        private Expression FinishListValue() {
            string proceedingWhiteSpace = _token.LeadingWhitespace;

            var oStart = GetStart();
            var oEnd = GetEnd();

            Expression ret;
            bool ateRightBracket;
            if (MaybeEat(TokenKind.RightBracket)) {
                ret = new ListExpression();
                ateRightBracket = true;
            } else {
                bool prevAllow = _allowIncomplete;
                try {
                    _allowIncomplete = true;
                    Expression t0 = ParseExpression();
                    if (MaybeEat(TokenKind.Comma)) {
                        string commaWhiteSpace = _token.LeadingWhitespace;
                        bool trailingComma;
                        List<string> listWhiteSpace;
                        var l = ParseTestListAsExpr(t0, out listWhiteSpace, out trailingComma);
                        ateRightBracket = Eat(TokenKind.RightBracket);
                        
                        ret = new ListExpression(l.ToArray());
                        
                        if (listWhiteSpace != null) {                            
                            AddListWhiteSpace(ret, listWhiteSpace.ToArray());
                        }
                    } else if (PeekToken(Tokens.KeywordForToken)) {
                        ret = FinishListComp(t0, out ateRightBracket);
                    } else {
                        ateRightBracket = Eat(TokenKind.RightBracket);
                        ret = new ListExpression(t0);
                    }
                } finally {
                    _allowIncomplete = prevAllow;
                }
            }

            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, proceedingWhiteSpace);
                AddSecondPrecedingWhiteSpace(ret, _token.LeadingWhitespace);
                if (!ateRightBracket) {
                    AddErrorMissingCloseGrouping(ret);
                }
            }

            var cStart = GetStart();
            var cEnd = GetEnd();

            ret.SetLoc(oStart, cEnd);
            return ret;
        }

        // list_iter ']'
        private ListComprehension FinishListComp(Expression item, out bool ateRightBracket) {
            ComprehensionIterator[] iters = ParseListCompIter();
            ateRightBracket = Eat(TokenKind.RightBracket);
            return new ListComprehension(item, iters);
        }

        // list_iter: list_for | list_if
        private ComprehensionIterator[] ParseListCompIter() {
            List<ComprehensionIterator> iters = new List<ComprehensionIterator>();
            ComprehensionFor firstFor = ParseListCompFor();

            iters.Add(firstFor);

            while (true) {
                ComprehensionIterator iterator;
                
                if (PeekToken(Tokens.KeywordForToken)) {
                    iterator = ParseListCompFor();
                } else if (PeekToken(Tokens.KeywordIfToken)) {
                    iterator = ParseCompIf();
                } else {
                    break;
                }

                iters.Add(iterator);
            }

            return iters.ToArray();
        }

        // list_for: 'for' target_list 'in' old_expression_list [list_iter]
        private ComprehensionFor ParseListCompFor() {
            Eat(TokenKind.KeywordFor);
            string startWhiteSpace = _token.LeadingWhitespace;
            var start = GetStart();
            bool trailingComma;
            List<string> listWhiteSpace;
            List<Expression> l = ParseExpressionList(out trailingComma, out listWhiteSpace);

            // expr list is something like:
            //  ()
            //  a
            //  a,b
            //  a,b,c
            // we either want just () or a or we want (a,b) and (a,b,c)
            // so we can do tupleExpr.EmitSet() or loneExpr.EmitSet()

            Expression lhs = MakeTupleOrExpr(l, listWhiteSpace, trailingComma, true);
            bool ateIn = Eat(TokenKind.KeywordIn);
            string inWhiteSpace;
            Expression list;

            if (ateIn) {
                inWhiteSpace = _token.LeadingWhitespace;
                if (_langVersion.Is3x()) {
                    list = ParseOrTest();
                } else {
                    list = ParseOldExpressionListAsExpr();
                }
            } else {
                inWhiteSpace = null;
                list = Error("");
            }

            ComprehensionFor ret = new ComprehensionFor(lhs, list);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, startWhiteSpace);
                if (inWhiteSpace != null) {
                    AddSecondPrecedingWhiteSpace(ret, inWhiteSpace);
                }
                if (!ateIn) {
                    AddErrorIsIncompleteNode(ret);
                }
            }

            ret.SetLoc(start, GetEnd());
            return ret;
        }

        // list_if: 'if' old_test [list_iter]
        // comp_if: 'if' old_test [comp_iter]
        private ComprehensionIf ParseCompIf() {
            var start = GetStart();
            Eat(TokenKind.KeywordIf);
            string ifWhiteSpace = _token.LeadingWhitespace;
            Expression expr = ParseOldExpression();
            var end = GetEnd();

            ComprehensionIf ret = new ComprehensionIf(expr);
            ret.HeaderIndex = end;
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, ifWhiteSpace);
            }
            ret.SetLoc(start, end);
            return ret;
        }

        private Expression FinishStringConversion() {
            Debug.Assert(_token.Token.Kind == TokenKind.BackQuote);
            string firstWhiteSpace = _token.LeadingWhitespace;
            Expression ret;
            var start = GetStart();
            Expression expr = ParseTestListAsExpr();
            bool ateBackQuote = Eat(TokenKind.BackQuote);
            ret = new BackQuoteExpression(expr);
            if (_verbatim) {
                AddPrecedingWhiteSpace(ret, firstWhiteSpace);
                AddSecondPrecedingWhiteSpace(ret, _token.LeadingWhitespace);
                if (!ateBackQuote) {
                    AddErrorMissingCloseGrouping(ret);
                }
            }
            ret.SetLoc(start, GetEnd());
            return ret;
        }

        private Expression MakeTupleOrExpr(List<Expression> l, List<string> itemWhiteSpace, bool trailingComma, bool parenFreeTuple = false) {
            return MakeTupleOrExpr(l, itemWhiteSpace, trailingComma, false, parenFreeTuple);
        }

        private Expression MakeTupleOrExpr(List<Expression> l, List<string> itemWhiteSpace, bool trailingComma, bool expandable, bool parenFreeTuple = false) {
            if (l.Count == 1 && !trailingComma) return l[0];

            Expression[] exprs = l.ToArray();
            TupleExpression te = new TupleExpression(expandable && !trailingComma, exprs);
            if (_verbatim) {
                if (itemWhiteSpace != null) {
                    AddListWhiteSpace(te, itemWhiteSpace.ToArray());
                }
                if (parenFreeTuple) {
                    AddIsAltForm(te);
                }
            }
            if (exprs.Length > 0) {
                te.SetLoc(exprs[0].StartIndex, exprs[exprs.Length - 1].EndIndex);
            }
            return te;
        }

        private static bool NeverTestToken(Token t) {
            switch (t.Kind) {
                case TokenKind.AddEqual:
                case TokenKind.SubtractEqual:
                case TokenKind.MultiplyEqual:
                case TokenKind.DivideEqual:
                case TokenKind.ModEqual:
                case TokenKind.BitwiseAndEqual:
                case TokenKind.BitwiseOrEqual:
                case TokenKind.ExclusiveOrEqual:
                case TokenKind.LeftShiftEqual:
                case TokenKind.RightShiftEqual:
                case TokenKind.PowerEqual:
                case TokenKind.FloorDivideEqual:

                case TokenKind.Indent:
                case TokenKind.Dedent:
                case TokenKind.NewLine:
                case TokenKind.EndOfFile:
                case TokenKind.Semicolon:

                case TokenKind.Assign:
                case TokenKind.RightBrace:
                case TokenKind.RightBracket:
                case TokenKind.RightParenthesis:

                case TokenKind.Comma:

                case TokenKind.Colon:

                case TokenKind.KeywordFor:
                case TokenKind.KeywordIn:
                case TokenKind.KeywordIf:
                    return true;

                default: return false;
            }
        }

        private FunctionDefinition CurrentFunction {
            get {
                if (_functions != null && _functions.Count > 0) {
                    return _functions.Peek();
                }
                return null;
            }
        }

        private FunctionDefinition PopFunction() {
            if (_functions != null && _functions.Count > 0) {
                return _functions.Pop();
            }
            return null;
        }

        private void PushFunction(FunctionDefinition function) {
            if (_functions == null) {
                _functions = new Stack<FunctionDefinition>();
            }
            _functions.Push(function);
        }

        private CallExpression FinishCallExpr(Expression target, params Arg[] args) {
            bool hasArgsTuple = false;
            bool hasKeywordDict = false;
            int keywordCount = 0;
            int extraArgs = 0;

            foreach (Arg arg in args) {
                if (arg.Name == null) {
                    if (hasArgsTuple || hasKeywordDict || keywordCount > 0) {
                        ReportSyntaxError(arg.StartIndex, arg.EndIndex, "non-keyword arg after keyword arg");
                    }
                } else if (arg.Name == "*") {
                    if (hasArgsTuple || hasKeywordDict) {
                        ReportSyntaxError(arg.StartIndex, arg.EndIndex, "only one * allowed");
                    }
                    hasArgsTuple = true; extraArgs++;
                } else if (arg.Name == "**") {
                    if (hasKeywordDict) {
                        ReportSyntaxError(arg.StartIndex, arg.EndIndex, "only one ** allowed");
                    }
                    hasKeywordDict = true; extraArgs++;
                } else {
                    if (hasKeywordDict) {
                        ReportSyntaxError(arg.StartIndex, arg.EndIndex, "keywords must come before ** args");
                    }
                    keywordCount++;
                }
            }

            return new CallExpression(target, args);
        }

        #endregion

        #region Implementation Details

        private ParseResult ParseFileWorker() {
            StartParsing();

            List<Statement> l = new List<Statement>();

            //
            // A future statement must appear near the top of the module. 
            // The only lines that can appear before a future statement are: 
            // - the module docstring (if any), 
            // - comments, 
            // - blank lines, and 
            // - other future statements. 
            // 

            MaybeEatNewLine();

            if (PeekKind(TokenKind.Constant)) {
                Statement s = ParseStmt();
                l.Add(s);
                _fromFutureAllowed = false;
                ExpressionStatement es = s as ExpressionStatement;
                if (es != null) {
                    ConstantExpression ce = es.Expression as ConstantExpression;
                    if (ce != null && IsString(ce)) {
                        // doc string
                        _fromFutureAllowed = true;
                    }
                }
            }

            MaybeEatNewLine();

            // from __future__
            if (_fromFutureAllowed) {
                while (PeekToken(Tokens.KeywordFromToken)) {
                    Statement s = ParseStmt();
                    l.Add(s);
                    FromImportStatement fis = s as FromImportStatement;
                    if (fis != null && !fis.IsFromFuture) {
                        // end of from __future__
                        break;
                    }
                }
            }

            // the end of from __future__ sequence
            _fromFutureAllowed = false;

            while (true) {
                if (MaybeEatEof()) break;
                if (MaybeEatNewLine()) continue;

                Statement s = ParseStmt();
                l.Add(s);
            }

            Statement[] stmts = l.ToArray();

            SuiteStatement ret = new SuiteStatement(stmts);
            AddIsAltForm(ret);
            if (_token.Token != null) {
                ret.SetLoc(0, GetEnd());
            }
            return CreateAst(ret);
        }

        private bool IsString(ConstantExpression ce) {
            if (_langVersion.Is3x()) {
                return ce.Value is string;
            }
            return ce.Value is AsciiString;
        }

        private Statement InternalParseInteractiveInput(out bool parsingMultiLineCmpdStmt, out bool isEmptyStmt) {
            Statement s;
            isEmptyStmt = false;
            parsingMultiLineCmpdStmt = false;

            switch (PeekKind()) {
                case TokenKind.NewLine:
                    MaybeEatNewLine();
                    if (!MaybeEat(TokenKind.EndOfFile)) {
                        parsingMultiLineCmpdStmt = true;
                        _errorCode = ErrorCodes.IncompleteStatement;
                    } else {
                        isEmptyStmt = true;
                    }
                    return null;

                case TokenKind.KeywordIf:
                case TokenKind.KeywordWhile:
                case TokenKind.KeywordFor:
                case TokenKind.KeywordTry:
                case TokenKind.At:
                case TokenKind.KeywordDef:
                case TokenKind.KeywordClass:
                case TokenKind.KeywordWith:
                    parsingMultiLineCmpdStmt = true;
                    s = ParseStmt();
                    EatEndOfInput();
                    break;
                case TokenKind.EndOfFile:
                    isEmptyStmt = true;
                    return null;
                default:
                    //  parseSimpleStmt takes care of one or more simple_stmts and the Newline
                    s = ParseSimpleStmt();
                    MaybeEatNewLine();
                    Eat(TokenKind.EndOfFile);
                    break;

            }
            return s;
        }



        private Expression ParseTestListAsExpression() {
            StartParsing();

            Expression expression = ParseTestListAsExpr();
            EatEndOfInput();
            return expression;
        }

        /// <summary>
        /// Maybe eats a new line token returning true if the token was
        /// eaten.
        /// 
        /// Python always tokenizes to have only 1  new line character in a 
        /// row.  But we also craete NLToken's and ignore them except for 
        /// error reporting purposes.  This gives us the same errors as 
        /// CPython and also matches the behavior of the standard library 
        /// tokenize module.  This function eats any present NL tokens and throws
        /// them away.
        /// 
        /// We also need to add the new lines into any proceeding white space
        /// when we're parsing in verbatim mode.
        /// </summary>
        private bool MaybeEatNewLine() {
            string newWhiteSpace;
            if (MaybeEatNewLine(out newWhiteSpace)) {
                if (_verbatim) {
                    DeferWhitespace(newWhiteSpace);
                }
                return true;
            }
            return false;
        }

        private bool MaybeEatNewLine(out string whitespace) {
            whitespace = _verbatim ? "" : null;
            if (MaybeEat(TokenKind.NLToken) || MaybeEat(TokenKind.NewLine)) {
                if (whitespace != null) {
                    whitespace += _token.LeadingWhitespace + _token.Token.VerbatimImage;
                }
                while (MaybeEat(TokenKind.NLToken)) {
                    if (whitespace != null) {
                        whitespace += _token.LeadingWhitespace + _token.Token.VerbatimImage;
                    }
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Eats a new line token throwing if the next token isn't a new line.  
        /// 
        /// Python always tokenizes to have only 1  new line character in a 
        /// row.  But we also craete NLToken's and ignore them except for 
        /// error reporting purposes.  This gives us the same errors as 
        /// CPython and also matches the behavior of the standard library 
        /// tokenize module.  This function eats any present NL tokens and throws
        /// them away.
        /// </summary>
        private bool EatNewLine(out string whitespace) {
            whitespace = _verbatim ? "" : null;
            if (MaybeEat(TokenKind.NLToken) || Eat(TokenKind.NewLine)) {
                if (whitespace != null) {
                    whitespace += _token.LeadingWhitespace + _token.Token.VerbatimImage;
                }

                while (MaybeEat(TokenKind.NLToken)) {
                    if (whitespace != null) {
                        whitespace += _token.LeadingWhitespace + _token.Token.VerbatimImage;
                    }
                }
                return true;
            }
            return false;
        }

        private Token EatEndOfInput() {
            while (MaybeEatNewLine() || MaybeEat(TokenKind.Dedent)) {
                ;
            }

            var t = Next();
            if (t.Token.Kind != TokenKind.EndOfFile) {
                ReportSyntaxError(t);
            }
            return t.Token;
        }

        private bool TrueDivision {
            get { return (_languageFeatures & FutureOptions.TrueDivision) == FutureOptions.TrueDivision; }
        }

        private bool AbsoluteImports {
            get { return (_languageFeatures & FutureOptions.AbsoluteImports) == FutureOptions.AbsoluteImports; }
        }

        private void StartParsing() {
            if (_parsingStarted)
                throw new InvalidOperationException("Parsing already started. Use Restart to start again.");

            _parsingStarted = true;

            string whitespace = _verbatim ? "" : null;
            while (MaybeEat(TokenKind.NLToken)) {
                if (whitespace != null) {
                    whitespace += _token.LeadingWhitespace + _token.Token.VerbatimImage;
                }
            }
            DeferWhitespace(whitespace);
        }

        private int GetEnd() {
            Debug.Assert(_token.Token != null, "No token fetched");
            return _token.Span.End;
        }

        private int GetStart() {
            Debug.Assert(_token.Token != null, "No token fetched");
            return _token.Span.Start;
        }

        private TokenWithSpan Next() {
            _token = Peek(1);
            _lookahead.RemoveAt(0);
            return _token;
        }

        private Token NextToken() {
            return Next().Token;
        }

        private Token PeekToken(int count = 1) {
            return Peek(count).Token;
        }

        private TokenKind PeekKind(int count = 1) {
            return Peek(count).Token.Kind;
        }

        private bool PeekKind(TokenKind kind, int count = 1) {
            return PeekKind(count) == kind;
        }

        private TokenWithSpan Peek(int count = 1) {
            while (_lookahead.Count < count) {
                _lookahead.Add(_FetchNext());
            }
            return _lookahead[count - 1];
        }

        private void EatPeek(int count = 1) {
            Peek(count);
            _lookahead.RemoveAt(count - 1);
        }

        private void DeferWhitespace(string whitespace, bool suffix = false) {
            var t = Peek();
            if (suffix) {
                _lookahead[0] = new TokenWithSpan(t.Token, t.Span, t.LeadingWhitespace + whitespace);
            } else {
                _lookahead[0] = new TokenWithSpan(t.Token, t.Span, whitespace + t.LeadingWhitespace);
            }
        }

        /// <summary>
        /// Fetches the next token.
        /// </summary>
        /// <remarks>
        /// Not for use outside the Peek function. The parser should always use
        /// <see cref="Next"/> or <see cref="Peek"/>.
        /// </remarks>
        private TokenWithSpan _FetchNext() {
            if (_eofToken.Token != null) {
                return _eofToken;
            }
            while (_tokens.MoveNext()) {
                var tws = _tokens.Current;
                if (tws.Token == null) {
                    break;
                } else if (tws.Token.Kind == TokenKind.EndOfFile) {
                    _eofToken = tws;
                }

                return tws;
            }
            return TokenWithSpan.Empty;
        }

        private bool PeekToken(Token check) {
            return PeekToken() == check;
        }

        private bool Eat(TokenKind kind) {
            if (PeekKind(kind)) {
                Next();
                return true;
            } else {
                ReportSyntaxError();
                return false;
            }
        }

        private bool MaybeEat(TokenKind kind) {
            if (PeekKind(kind)) {
                Next();
                return true;
            } else {
                return false;
            }
        }

        private bool MaybeEatName(string name) {
            var peeked = PeekToken();
            if (peeked.Kind == TokenKind.Name && ((NameToken)peeked).Name == name) {
                Next();
                return true;
            } else {
                return false;
            }
        }

        #endregion

        #region Verbatim AST support

        private void AddPrecedingWhiteSpace(Node ret) {
            AddPrecedingWhiteSpace(ret, _token.LeadingWhitespace);
        }

        private Dictionary<object, object> GetNodeAttributes(Node node) {
            Dictionary<object, object> attrs;
            if (!_attributes.TryGetValue(node, out attrs)) {
                _attributes[node] = attrs = new Dictionary<object, object>();
            }
            return attrs;
        }

        private void AddVerbatimName(Name name, Node ret) {
            if (_verbatim && name.RealName != name.VerbatimName) {
                GetNodeAttributes(ret)[NodeAttributes.VerbatimImage] = name.VerbatimName;
            }
        }

        private void AddVerbatimImage(Node ret, string image) {
            if (_verbatim) {
                GetNodeAttributes(ret)[NodeAttributes.VerbatimImage] = image;
            }
        }

        private List<string> MakeWhiteSpaceList() {
            return _verbatim ? new List<string>() : null;
        }

        private void AddTrailingWhiteSpace(Node ret, string whiteSpace) {
            Debug.Assert(_verbatim);
            GetNodeAttributes(ret)[NodeAttributes.TrailingWhiteSpace] = whiteSpace;
        }

        private void AddPrecedingWhiteSpace(Node ret, string whiteSpace) {
            Debug.Assert(_verbatim);
            GetNodeAttributes(ret)[NodeAttributes.PrecedingWhiteSpace] = whiteSpace;
        }

        private void AddSecondPrecedingWhiteSpace(Node ret, string whiteSpace) {
            if (_verbatim) {
                Debug.Assert(_verbatim);
                GetNodeAttributes(ret)[NodeAttributes.SecondPrecedingWhiteSpace] = whiteSpace;
            }
        }

        private void AddThirdPrecedingWhiteSpace(Node ret, string whiteSpace) {
            Debug.Assert(_verbatim);
            GetNodeAttributes(ret)[NodeAttributes.ThirdPreceedingWhiteSpace] = whiteSpace;
        }

        private void AddFourthPrecedingWhiteSpace(Node ret, string whiteSpace) {
            Debug.Assert(_verbatim);
            GetNodeAttributes(ret)[NodeAttributes.FourthPrecedingWhiteSpace] = whiteSpace;
        }

        private void AddFifthPrecedingWhiteSpace(Node ret, string whiteSpace) {
            Debug.Assert(_verbatim);
            GetNodeAttributes(ret)[NodeAttributes.FifthPrecedingWhiteSpace] = whiteSpace;
        }

        private void AddExtraVerbatimText(Node ret, string text) {
            Debug.Assert(_verbatim);
            GetNodeAttributes(ret)[NodeAttributes.ExtraVerbatimText] = text;
        }

        private void AddListWhiteSpace(Node ret, string[] whiteSpace) {
            Debug.Assert(_verbatim);
            GetNodeAttributes(ret)[NodeAttributes.ListWhiteSpace] = whiteSpace;
        }

        private void AddNamesWhiteSpace(Node ret, string[] whiteSpace) {
            Debug.Assert(_verbatim);
            GetNodeAttributes(ret)[NodeAttributes.NamesWhiteSpace] = whiteSpace;
        }

        private void AddVerbatimNames(Node ret, string[] names) {
            Debug.Assert(_verbatim);
            GetNodeAttributes(ret)[NodeAttributes.VerbatimNames] = names;
        }

        private void AddIsAltForm(Node expr) {
            GetNodeAttributes(expr)[NodeAttributes.IsAltFormValue] = NodeAttributes.IsAltFormValue;
        }

        private void AddErrorMissingCloseGrouping(Node expr) {
            GetNodeAttributes(expr)[NodeAttributes.ErrorMissingCloseGrouping] = NodeAttributes.ErrorMissingCloseGrouping;
        }

        private void AddErrorIsIncompleteNode(Node expr) {
            GetNodeAttributes(expr)[NodeAttributes.ErrorIncompleteNode] = NodeAttributes.ErrorIncompleteNode;
        }

        private void AddTrailingComment(Node expr, string comment) {
            GetNodeAttributes(expr)[NodeAttributes.Comment] = comment;
        }

        #endregion
    }
}
