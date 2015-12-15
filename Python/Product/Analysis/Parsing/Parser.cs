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
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.PythonTools.Analysis.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Parsing {
    public class Parser {
        private readonly Tokenization _tokenization;
        private readonly PythonLanguageVersion _version;

        private IEnumerator<Token> _tokenEnumerator;
        private Token _current;
        private List<Token> _lookahead;
        private string _currentIndent;
        private int _currentIndentLength;
        private bool _singleLine;
        private ErrorSink _errors;
        private Token _eofToken;
        private Stack<ScopeStatement> _scopes;
        private Severity _indentationInconsistencySeverity;
        private List<Statement> _decorators;

        public Parser(Tokenization tokenization) {
            _tokenization = tokenization;
            _version = _tokenization.LanguageVersion;
            _features = new LanguageFeatures(_version, FutureOptions.None);
        }

        private void Reset() {
            _tokenEnumerator = _tokenization.AllTokens.GetEnumerator();
            _lookahead = new List<Token>();
            _scopes = new Stack<ScopeStatement>();
        }

        internal Tokenization Tokenization => _tokenization;

        public Severity IndentationInconsistencySeverity {
            get { return _indentationInconsistencySeverity; }
            set { _indentationInconsistencySeverity = value; }
        }

        #region Language Features

        internal LanguageFeatures _features;

        internal ScopeStatement CurrentScope => _scopes.Any() ? _scopes.Peek() : null;
        internal ScopeStatement CurrentClass => _scopes.OfType<ClassDefinition>().FirstOrDefault();
        internal bool IsInAsyncFunction => CurrentScope?.IsAsync ?? false;
        internal bool IsInFunction => CurrentScope is FunctionDefinition;
        internal bool IsInClass => CurrentScope is ClassDefinition;

        #endregion

        #region Parsing

        public PythonAst Parse(ErrorSink errors = null) {
            Reset();
            try {
                _errors = errors ?? ErrorSink.Null;
                return new PythonAst(ParseSuite(assumeMultiLine: true), _tokenization, _features);
            } finally {
                _errors = null;
            }
        }

        private SuiteStatement ParseSuite(bool assumeMultiLine = false) {
            var body = new List<Statement>();

            var prevIndent = _currentIndent;
            var prevIndentLength = _currentIndentLength;
            var prevSingleLine = _singleLine;

            SuiteStatement suite;
            Statement stmt;

            _singleLine = !assumeMultiLine && !TryRead(TokenKind.NewLine);

            //while (!_singleLine && !TryRead(TokenKind.SignificantWhitespace)) {
            //    var ws = ReadWhitespace();
            //    Debug.Assert(Peek.IsAny(TokenKind.Comment, TokenKind.NewLine, TokenKind.EndOfFile));
            //    stmt = new EmptyStatement {
            //        BeforeNode = ws,
            //        AfterNode = ReadNewLine()
            //    };
            //    stmt.Freeze();
            //    body.Add(stmt);
            //    if (Peek.Is(TokenKind.EndOfFile)) {
            //        suite = new SuiteStatement(body, ws);
            //        suite.Freeze();
            //        return suite;
            //    }
            //}

            var ws = ReadWhitespace();

            var indent = TryRead(TokenKind.SignificantWhitespace) ? Current.Span : SourceSpan.None;
            _currentIndent = _tokenization.GetTokenText(indent);
            _currentIndentLength = GetIndentLength(_currentIndent);

            // Leading whitespace is already read; it will be attached to the
            // suite rather than each statement.
            stmt = ParseStmt();
            stmt.BeforeNode = ws;

            while (IsCurrentStatementBreak()) {
                stmt.AfterNode = ReadCurrentStatementBreak(stmt is CompoundStatement);

                if (!HandleDecorators(body, stmt)) {
                    stmt.Freeze();
                    body.Add(stmt);
                }

                ws = ReadWhitespace();
                stmt = ParseStmt();
                stmt.BeforeNode = ws;
            }

            if (!HandleDecorators(body, stmt)) {
                stmt.Freeze();
                body.Add(stmt);
            }
            if (_decorators != null) {
                foreach (var d in _decorators) {
                    body.Add(d);
                    ReportError(InvalidDecoratorError, d.Span);
                }
                _decorators = null;
            }

            _currentIndent = prevIndent;
            _currentIndentLength = prevIndentLength;
            _singleLine = prevSingleLine;

            suite = WithTrailingWhitespace(new SuiteStatement(body, indent));
            suite.Freeze();

            return suite;
        }

        private string InvalidDecoratorError {
            get {
                return "invalid decorator, must be applied to function" + (_features.HasClassDecorators ? " or class" : "");
            }
        }

        private bool HandleDecorators(List<Statement> suite, Statement stmt) {
            if (stmt is DecoratorStatement) {
                if (_decorators == null) {
                    _decorators = new List<Statement> { stmt };
                } else {
                    _decorators.Add(stmt);
                }
                stmt.Freeze();
                return true;
            } else if (stmt is EmptyStatement) {
                if (_decorators == null) {
                    return false;
                }
                _decorators.Add(stmt);
                stmt.Freeze();
                return true;
            } else if (_decorators != null) {
                foreach (var d in _decorators) {
                    suite.Add(d);
                    ReportError(InvalidDecoratorError, d.Span);
                }
                _decorators = null;
            }
            return false;
        }

        private bool IsCurrentStatementBreak(TokenKind nextToken = TokenKind.Unknown) {
            var token = GetTokenAfterCurrentStatementBreak();
            if (token.Is(TokenKind.Unknown)) {
                return false;
            }
            return nextToken == TokenKind.Unknown || token.Is(nextToken);
        }

        private int GetIndentLength(Token token) {
            return GetIndentLength(_tokenization.GetTokenText(token));
        }

        private int GetIndentLength(string s) {
            int length = 0;
            foreach (var c in s) {
                if (c == ' ') {
                    length += 1;
                } else if (c == '\t') {
                    length += 8;
                } else {
                    break;
                }
            }
            return length;
        }

        private Token GetTokenAfterCurrentStatementBreak() {
            Token p2;

            // Allow semicolons to separate statements anywhere
            if (Peek.Is(TokenKind.SemiColon)) {
                p2 = PeekAhead(2);
                if (p2.Is(TokenCategory.Whitespace)) {
                    return PeekAhead(3);
                }
                return p2;
            }

            // EOF always indicates end of suite
            if (Peek.Is(TokenKind.EndOfFile)) {
                return Token.Empty;
            }

            // No other ways to separate statements on single-line suites
            if (_singleLine) {
                return Token.Empty;
            }

            // If not the end of a statement, it's not the end of a statement
            if (!Peek.Is(TokenUsage.EndStatement)) {
                return Token.Empty;
            }

            // Check the significant whitespace (if any)
            int lookaheadCount = 1;
            while (!(p2 = PeekAhead(++lookaheadCount)).IsAny(
                TokenKind.SignificantWhitespace,
                TokenKind.EndOfFile,
                TokenKind.NewLine
            )) { }
            if (p2.Is(TokenKind.SignificantWhitespace) && GetIndentLength(p2) >= _currentIndentLength) {
                // Keep going if it's an unexpected indent - we'll add the
                // error when we read the whitespace.
                return PeekAhead(lookaheadCount + 1);
            }
            if (p2.Is(TokenKind.NewLine)) {
                // Report an empty line as part of the current block
                return p2;
            }

            // Whitespace does not match expectation
            return Token.Empty;
        }

        private SourceSpan ReadCurrentStatementBreak(bool justDedented = false) {
            Debug.Assert(IsCurrentStatementBreak());

            var start = Peek.Span.Start;
            var end = Peek.Span.End;
            while (!Peek.IsAny(TokenKind.SignificantWhitespace, TokenKind.SemiColon, TokenKind.EndOfFile)) {
                end = Next().Span.End;
            }

            if (TryRead(TokenKind.SignificantWhitespace) ||
                TryRead(TokenKind.NewLine) && TryRead(TokenKind.SignificantWhitespace)) {
                var ws = Current;
                end = ws.Span.Start;
                var text = _tokenization.GetTokenText(ws);
                if (GetIndentLength(text) > _currentIndentLength) {
                    if (justDedented) {
                        _errors.Add("unindent does not match any outer indentation level", ws.Span, 0, Severity.Error);
                    } else {
                        _errors.Add("unexpected indent", ws.Span, 0, Severity.Error);
                    }
                }
                if (text != _currentIndent && _indentationInconsistencySeverity != Severity.Ignore) {
                    _errors.Add("inconsistent whitespace", ws.Span, 0, _indentationInconsistencySeverity);
                }
            } else {
                end = Next().Span.End;
            }
            return new SourceSpan(start, end);
        }

        private Statement ParseStmt() {
            Statement stmt = null;
            var start = Peek.Span.Start;

            try {
                if (Peek.Is(TokenUsage.EndStatement)) {
                    stmt = WithTrailingWhitespace(new EmptyStatement {
                        Span = new SourceSpan(Peek.Span.Start, 0)
                    });
                } else if (Peek.Is(TokenKind.At)) {
                    stmt = ParseDecorator();
                } else if (Peek.IsAny(TokenUsage.BeginStatement, TokenUsage.BeginStatementOrBinaryOperator) ||
                    (Peek.Is(TokenKind.KeywordAsync) && _features.HasAsyncAwait)) {
                    stmt = ParseIdentifierAsStatement();
                } else {
                    stmt = ParseExprStmt();
                }

                if (stmt == null) {
                    throw new InvalidOperationException(string.Format(
                        "Failed to parse {0}: '{1}'",
                        Peek.Kind,
                        _tokenization.GetTokenText(Peek)
                    ));
                }

                Debug.Assert(stmt.BeforeNode.Length == 0);

                if (!Peek.Is(TokenUsage.EndStatement)) {
                    throw new ParseErrorException("invalid syntax", Peek.Span.Start);
                }
            } catch (ParseErrorException ex) {
                ReadUntil(IsEndOfStatement);
                stmt = new ErrorStatement(ex.Message) {
                    Span = new SourceSpan(start, Current.Span.End)
                };
                ReportError(ex.Message, new SourceSpan(ex.Location, Current.Span.End));
            }

            return stmt;
        }

        private Statement ParseIdentifierAsStatement() {
            var kind = Peek.Kind;
            bool isAsync = false;
            if (_features.HasAsyncAwait && kind == TokenKind.KeywordAsync) {
                kind = PeekAhead(3).Kind;
                if (kind != TokenKind.KeywordFor &&
                    kind != TokenKind.KeywordDef &&
                    kind != TokenKind.KeywordWith
                ) {
                    // async prefix is not supported on these keywords, so
                    // parse as an expression 
                    return ParseExprStmt();
                }
                isAsync = true;
            }

            switch (kind) {
                case TokenKind.KeywordIf:
                    return ParseIfStmt();
                case TokenKind.KeywordWhile:
                    return ParseWhileStmt();
                case TokenKind.KeywordFor:
                    return ParseForStmt(isAsync);
                case TokenKind.KeywordTry:
                    return ParseTryStmt();
                case TokenKind.At:
                    Debug.Fail("Should have been handled earlier");
                    return ParseDecorator();
                case TokenKind.KeywordDef:
                    return ParseFuncDef(isAsync);
                case TokenKind.KeywordClass:
                    return ParseClassDef();
                case TokenKind.KeywordWith:
                    if (_features.HasWith) {
                        return ParseWithStmt(isAsync);
                    }
                    goto default;
                case TokenKind.KeywordPrint:
                    if (!_features.HasPrintFunction) {
                        return ParsePrintStmt();
                    }
                    goto default;
                case TokenKind.KeywordPass:
                    return WithTrailingWhitespace(new PassStatement {
                        Span = Read(TokenKind.KeywordPass),
                    });
                case TokenKind.KeywordBreak:
                    return WithTrailingWhitespace(new BreakStatement {
                        Span = Read(TokenKind.KeywordBreak),
                    });
                case TokenKind.KeywordContinue:
                    return WithTrailingWhitespace(new ContinueStatement {
                        Span = Read(TokenKind.KeywordContinue),
                    });
                case TokenKind.KeywordReturn:
                    return ParseReturnStmt();
                case TokenKind.KeywordFrom:
                    return ParseFromImportStmt();
                case TokenKind.KeywordImport:
                    return ParseImportStmt();
                case TokenKind.KeywordGlobal:
                    return ParseGlobalStmt();
                case TokenKind.KeywordNonlocal:
                    if (_features.HasNonlocal) {
                        return ParseNonlocalStmt();
                    }
                    goto default;
                case TokenKind.KeywordRaise:
                    return ParseRaiseStmt();
                case TokenKind.KeywordAssert:
                    return ParseAssertStmt();
                case TokenKind.KeywordExec:
                    if (_features.HasExecStatement) {
                        return ParseExecStmt();
                    }
                    goto default;
                case TokenKind.KeywordDel:
                    return ParseDelStmt();
                default:
                    return ParseExprStmt();
            }
        }

        private Statement ParseIfStmt() {
            var stmt = new IfStatement() {
                Span = Peek.Span
            };

            bool moreTests = true;
            while (moreTests) {
                var ws = ReadWhitespace();
                var test = new CompoundStatement(Next().Kind) {
                    BeforeNode = ws,
                    Span = Current.Span
                };
                if (Current.IsAny(TokenKind.KeywordIf, TokenKind.KeywordElseIf)) {
                    test.Expression = ParseSingleExpression();
                }
                ReadCompoundStatement(test);

                moreTests = false;
                var nextStatement = GetTokenAfterCurrentStatementBreak();
                if (nextStatement.IsAny(TokenKind.KeywordElseIf, TokenKind.KeywordElse)) {
                    test.AfterNode = ReadCurrentStatementBreak();
                    moreTests = true;
                }
                test.Freeze();

                stmt.AddTest(test);
            }

            stmt.Span = new SourceSpan(stmt.Span.Start, Current.Span.Start);
            return stmt;
        }

        private Statement ParseWhileStmt() {
            var stmt = new WhileStatement() {
                BeforeNode = ReadWhitespace(),
                Span = Read(TokenKind.KeywordWhile),
                Expression = ParseSingleExpression()
            };

            ReadCompoundStatement(stmt);

            if (IsCurrentStatementBreak(TokenKind.KeywordElse)) {
                stmt.AfterBody = ReadCurrentStatementBreak();

                var elseStmt = new CompoundStatement(TokenKind.KeywordElse) {
                    BeforeNode = ReadWhitespace()
                };
                Read(TokenKind.KeywordElse);
                ReadCompoundStatement(elseStmt);

                stmt.Else = elseStmt;
            }

            stmt.Span = new SourceSpan(stmt.Span.Start, Current.Span.Start);
            return stmt;
        }

        private Statement ParseForStmt(bool isAsync) {
            var stmt = new ForStatement();
            if (isAsync) {
                if (!IsInAsyncFunction) {
                    ReportError("'async for' outside of async function");
                }
                stmt.Span = Read(TokenKind.KeywordAsync);
                stmt.AfterAsync = ReadWhitespace();
                Read(TokenKind.KeywordFor);
            } else {
                stmt.Span = Read(TokenKind.KeywordFor);
            }

            stmt.Index = ParseAssignmentTarget();
            Read(TokenKind.KeywordIn);
            stmt.Expression = ParseExpression(allowGenerator: false);

            ReadCompoundStatement(stmt);

            if (IsCurrentStatementBreak(TokenKind.KeywordElse)) {
                stmt.AfterBody = ReadCurrentStatementBreak();

                Read(TokenKind.KeywordElse);
                stmt.Else = new CompoundStatement(TokenKind.KeywordElse);
                ReadCompoundStatement(stmt.Else);
                stmt.Else.Freeze();
            }

            stmt.Span = new SourceSpan(stmt.Span.Start, Current.Span.Start);
            return stmt;
        }

        private Statement ParseTryStmt() {
            TokenKind[] HandlerKinds = new[] {
                TokenKind.KeywordExcept,
                TokenKind.KeywordFinally,
                TokenKind.KeywordElse
            };

            var stmt = new TryStatement {
                Span = Read(TokenKind.KeywordTry)
            };

            ReadCompoundStatement(stmt);

            var nextStatement = GetTokenAfterCurrentStatementBreak();
            if (nextStatement.IsAny(HandlerKinds)) {
                stmt.AfterBody = ReadCurrentStatementBreak();
            }

            while (nextStatement.IsAny(HandlerKinds)) {
                var ws = ReadWhitespace();
                var handler = new CompoundStatement(Next().Kind) {
                    BeforeNode = ws,
                    Span = Current.Span
                };
                if (handler.Kind == TokenKind.KeywordExcept && !PeekNonWhitespace.Is(TokenKind.Colon)) {
                    handler.Expression = ParseAsExpressions();
                }
                ReadCompoundStatement(handler);

                AsExpression ae;
                if (_version < PythonLanguageVersion.V26 && (ae = handler.Expression as AsExpression) != null) {
                    ReportError("'as' requires Python 2.6 or later", ae.Span);
                }

                TupleExpression te;
                if ((te = handler.Expression as TupleExpression) != null) {
                    if (te.Items.Count == 2) {
                        if (_version >= PythonLanguageVersion.V30) {
                            ReportError(
                                "\", variable\" not allowed in 3.x - use \"as variable\" instead.",
                                new SourceSpan(
                                    te.Items[0].Span.End,
                                    te.Items[1].Span.End
                                )
                            );
                        }
                    } else if (te.Items.Count > 2) {
                        ReportError(errorAt: te.Items[1].AfterNode);
                    }
                }

                nextStatement = GetTokenAfterCurrentStatementBreak();
                if (nextStatement.IsAny(HandlerKinds)) {
                    handler.AfterNode = ReadCurrentStatementBreak();
                }
                handler.Freeze();

                stmt.AddHandler(handler);
            }

            stmt.Span = new SourceSpan(stmt.Span.Start, Current.Span.Start);
            return stmt;
        }

        private Statement ParseDecorator() {
            var start = Peek.Span.Start;
            Read(TokenKind.At);
            return WithTrailingWhitespace(new DecoratorStatement {
                Expression = ParsePrimaryWithTrailers(),
                Span = new SourceSpan(start, Current.Span.End)
            });
        }

        private Statement ParseFuncDef(bool isCoroutine) {
            var start = Peek.Span.Start;
            var stmt = new FunctionDefinition(TokenKind.KeywordDef) {
                Decorators = _decorators
            };
            _decorators = null;

            if (isCoroutine) {
                Read(TokenKind.KeywordAsync);
                stmt.AfterAsync = ReadWhitespace();
            }
            Read(TokenKind.KeywordDef);

            _scopes.Push(stmt);
            try {
                stmt.NameExpression = ReadMemberName();
                Read(TokenKind.LeftParenthesis);
                stmt.Parameters = ParseParameterList();
                Read(TokenKind.RightParenthesis);
                var ws = ReadWhitespace();
                if (TryRead(TokenKind.Arrow)) {
                    stmt.BeforeReturnAnnotation = ws;
                    stmt.ReturnAnnotation = ParseExpression();

                    if (!_features.HasAnnotations) {
                        ReportError("invalid syntax, function annotations require 3.x", stmt.ReturnAnnotation.Span);
                    }

                    ws = ReadWhitespace();
                }
                stmt.BeforeColon = ws;
                Read(TokenKind.Colon);
                stmt.AfterColon = ReadWhitespace();

                stmt.Body = ParseSuite();
            } finally {
                _scopes.Pop();
            }

            if (stmt.IsGenerator && !_features.HasGeneratorReturn) {
                stmt.Walk(new ReturnInGeneratorErrorWalker {
                    Parser = this,
                    StartFuncDef = stmt
                });
            }

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return stmt;
        }

        private class ReturnInGeneratorErrorWalker : PythonWalker {
            public Parser Parser { get; set; }
            public FunctionDefinition StartFuncDef { get; set; }

            public override bool Walk(ClassDefinition node) {
                return false;
            }

            public override bool Walk(FunctionDefinition node) {
                return node == StartFuncDef;
            }

            public override bool Walk(ReturnStatement node) {
                if (!node.IsExpressionEmpty) {
                    Parser.ReportError("'return' with argument inside generator", node.Span);
                }
                return false;
            }
        }

        private Statement ParseClassDef() {
            var start = Peek.Span.Start;
            Read(TokenKind.KeywordClass);

            var stmt = new ClassDefinition {
                NameExpression = ReadMemberName(),
                Decorators = _decorators
            };
            if (!_features.HasClassDecorators && (_decorators?.Any() ?? false)) {
                foreach (var d in _decorators) {
                    ReportError("invalid syntax, class decorators require 2.6 or later.", d.Span);
                }
            }
            _decorators = null;

            if (TryRead(TokenKind.LeftParenthesis)) {
                stmt.Bases = ParseArgumentList(TokenKind.RightParenthesis, true);

                if (_version.Is2x()) {
                    foreach (var b in stmt.Bases) {
                        if (!string.IsNullOrEmpty(b.Name)) {
                            ReportError(errorAt: b.Span);
                        }
                    }
                }

                Read(TokenKind.RightParenthesis);
            }
            stmt.BeforeColon = ReadWhitespace();
            Read(TokenKind.Colon);
            stmt.AfterColon = ReadWhitespace();
            _scopes.Push(stmt);
            try {
                stmt.Body = ParseSuite();
            } finally {
                _scopes.Pop();
            }

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return stmt;
        }

        private Statement ParseWithStmt(bool isAsync) {
            var stmt = new WithStatement();
            if (isAsync) {
                if (!IsInAsyncFunction) {
                    ReportError("'async with' outside of async function");
                }
                stmt.Span = Read(TokenKind.KeywordAsync);
                stmt.AfterAsync = ReadWhitespace();
                Read(TokenKind.KeywordWith);
            } else {
                stmt.Span = Read(TokenKind.KeywordWith);
            }

            stmt.Expression = ParseAsExpressions();

            ReadCompoundStatement(stmt);
            return stmt;
        }

        private Statement ParsePrintStmt() {
            var start = Read(TokenKind.KeywordPrint).Start;

            var stmt = new PrintStatement();

            if (PeekNonWhitespace.Is(TokenKind.RightShift)) {
                stmt.BeforeLeftShift = ReadWhitespace();
                Read(TokenKind.RightShift);
                stmt.BeforeDestination = ReadWhitespace();
                stmt.Destination = ParseSingleExpression();
                if (!TryRead(TokenKind.Comma)) {
                    stmt.Span = new SourceSpan(start, Current.Span.End);
                    return stmt;
                }
            }

            var expr = ParseSingleExpression();
            if (expr is EmptyExpression) {
                return stmt;
            }
            expr.Freeze();
            stmt.AddExpression(expr);

            while (TryRead(TokenKind.Comma)) {
                expr = ParseSingleExpression();
                expr.Freeze();
                stmt.AddExpression(expr);
            }

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return stmt;
        }

        private Statement ParseReturnStmt() {
            var start = Read(TokenKind.KeywordReturn).Start;
            var func = CurrentScope as FunctionDefinition;
            if (func == null) {
                ReportError(errorAt: Current.Span);
            }

            var stmt = new ReturnStatement {
                Expression = ParseExpression()
            };

            if (func != null && stmt.Expression != null) {
                func.AddReturn(stmt.Expression);
            }

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return WithTrailingWhitespace(stmt);
        }

        private Statement ParseFromImportStmt() {
            var start = Read(TokenKind.KeywordFrom).Start;
            var stmt = new FromImportStatement {
                Root = ReadDottedName()
            };
            Read(TokenKind.KeywordImport);
            if (string.IsNullOrEmpty(stmt.Root.MakeString())) {
                ReportError("missing module name", Current.Span);
            }

            stmt.BeforeNames = ReadWhitespace();
            stmt.HasParentheses = TryRead(TokenKind.LeftParenthesis);

            bool hasStar = false;
            var names = new List<SequenceItemExpression>();
            var asNames = new List<NameExpression>();
            var name = TryReadName(allowStar: true);
            while (name != null) {
                Expression nameExpr;
                if (TryRead(TokenKind.KeywordAs)) {
                    var asStart = Current.Span.Start;
                    nameExpr = new AsExpression {
                        Expression = name,
                        NameExpression = ReadName(),
                        Span = new SourceSpan(name.Span.Start, Current.Span.End)
                    };
                    if (name.IsStar) {
                        ReportError(errorAt: new SourceSpan(asStart, Current.Span.End));
                    }
                } else {
                    nameExpr = name;
                }

                names.Add(WithTrailingWhitespace(new SequenceItemExpression {
                    Expression = nameExpr,
                    Span = new SourceSpan(name.Span.Start, Current.Span.End),
                    HasComma = TryRead(TokenKind.Comma)
                }));

                if (name.IsStar) {
                    hasStar = true;
                    break;
                }

                name = TryReadName(allowStar: true);
            }

            stmt.Names = names;
            var lastName = names.LastOrDefault();
            if ((lastName?.HasComma ?? false) && !stmt.HasParentheses) {
                ReportError("trailing comma not allowed without surrounding parentheses", new SourceSpan(lastName.Span.End, 1));
            }

            if (stmt.HasParentheses) {
                Read(TokenKind.RightParenthesis);
            }

            stmt.Span = new SourceSpan(start, Current.Span.End);
            if (hasStar && _version.Is3x() && CurrentScope != null) {
                ReportError("import * only allowed at module level", stmt.Span);
            }

            if (stmt.Root.IsFuture) {
                foreach (var n in stmt.Names) {
                    var ne = (n?.Expression as NameExpression) ?? (n?.Expression as AsExpression)?.Expression as NameExpression;
                    if (string.IsNullOrEmpty(ne?.Name)) {
                        continue;
                    }
                    switch (ne.Name) {
                        case "division":
                            _features.AddFuture(FutureOptions.TrueDivision);
                            break;
                        case "generators":
                            break;
                        case "with_statement":
                            _features.AddFuture(FutureOptions.WithStatement);
                            break;
                        case "absolute_import":
                            _features.AddFuture(FutureOptions.AbsoluteImports);
                            break;
                        case "print_function":
                            _features.AddFuture(FutureOptions.PrintFunction);
                            if (_version < PythonLanguageVersion.V26) {
                                ReportError("future feature is not defined until 2.6: print_function", n.Span);
                            }
                            break;
                        case "unicode_literals":
                            _features.AddFuture(FutureOptions.UnicodeLiterals);
                            if (_version < PythonLanguageVersion.V26) {
                                ReportError("future feature is not defined until 2.6: unicode_literals", n.Span);
                            }
                            break;
                        case "generator_stop":
                            _features.AddFuture(FutureOptions.GeneratorStop);
                            if (_version < PythonLanguageVersion.V35) {
                                ReportError("future feature is not defined until 3.5: generator_stop", n.Span);
                            }
                            break;
                    }
                }
            }

            return WithTrailingWhitespace(stmt);
        }

        private Statement ParseImportStmt() {
            var start = Read(TokenKind.KeywordImport).Start;
            var stmt = new ImportStatement();

            var names = new List<SequenceItemExpression>();
            var nameExpr = ReadDottedName();
            var expr = (Expression)nameExpr;
            while (!string.IsNullOrEmpty(nameExpr.MakeString())) {
                if (TryRead(TokenKind.KeywordAs)) {
                    expr = new AsExpression {
                        Expression = nameExpr,
                        NameExpression = ReadName(),
                        Span = new SourceSpan(nameExpr.Span.Start, Current.Span.End)
                    };
                }

                nameExpr = WithTrailingWhitespace(nameExpr);
                nameExpr.Freeze();

                names.Add(WithTrailingWhitespace(new SequenceItemExpression {
                    Expression = expr,
                    HasComma = TryRead(TokenKind.Comma),
                    Span = new SourceSpan(nameExpr.Span.Start, Current.Span.End)
                }));

                nameExpr = ReadDottedName();
                expr = nameExpr;
            }

            var lastName = names.LastOrDefault();
            if (lastName?.HasComma ?? false) {
                ReportError("trailing comma not allowed", lastName.Span);
            }

            stmt.Names = names;

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return WithTrailingWhitespace(stmt);
        }

        private Statement ParseGlobalStmt() {
            var start = Read(TokenKind.KeywordGlobal).Start;
            var stmt = new GlobalStatement();

            var func = CurrentScope as FunctionDefinition;
            do {
                var name = ReadName();
                stmt.AddName(name);
                func?.AddReferencedGlobal(name.Name);
            } while (TryRead(TokenKind.Comma));

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return WithTrailingWhitespace(stmt);
        }

        private Statement ParseNonlocalStmt() {
            var start = Read(TokenKind.KeywordNonlocal).Start;

            if (CurrentScope == null) {
                ReportError("nonlocal declaration not allowed at module level", Current.Span);
            }

            var stmt = new NonlocalStatement();

            var func = CurrentScope as FunctionDefinition;
            do {
                var name = ReadName();
                stmt.AddName(name);
                func?.AddReferencedNonLocal(name.Name);
            } while (TryRead(TokenKind.Comma));

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return WithTrailingWhitespace(stmt);
        }

        private Statement ParseRaiseStmt() {
            var start = Read(TokenKind.KeywordRaise).Start;
            var stmt = new RaiseStatement {
                Expression = ParseExpression()
            };

            var te = stmt.Expression as TupleExpression;
            if (te != null && _version.Is3x()) {
                ReportError(
                    "invalid syntax, only exception value is allowed in 3.x.",
                    te.Count < 1 ? stmt.Expression.Span : new SourceSpan(te.Items[0].Span.End, stmt.Expression.Span.End)
                );
            }
            if (TryRead(TokenKind.KeywordFrom)) {
                var fromStart = Current.Span.Start;
                stmt.Cause = ParseExpression();
                if (_version.Is2x()) {
                    ReportError("invalid syntax, from cause not allowed in 2.x.", new SourceSpan(fromStart, Current.Span.End));
                }
            }

            stmt.Span = new SourceSpan(start, Current.Span.End);

            return WithTrailingWhitespace(stmt);
        }

        private Statement ParseAssertStmt() {
            var start = Read(TokenKind.KeywordAssert).Start;
            var stmt = new AssertStatement {
                Expression = ParseExpression()
            };

            stmt.Span = new SourceSpan(start, Current.Span.End);

            return WithTrailingWhitespace(stmt);
        }

        private Statement ParseExecStmt() {
            var start = Read(TokenKind.KeywordExec).Start;

            var stmt = new ExecStatement {
                Expression = ParseExpression()
            };
            stmt.Span = new SourceSpan(start, Current.Span.End);

            stmt.CheckSyntax(this);

            return WithTrailingWhitespace(stmt);
        }

        private Statement ParseDelStmt() {
            var start = Read(TokenKind.KeywordDel).Start;

            var stmt = new DelStatement {
                Expression = ParseExpression()
            };
            stmt.Span = new SourceSpan(start, Current.Span.End);

            stmt.Expression.CheckDelete(this);

            return WithTrailingWhitespace(stmt);
        }

        private Statement ParseExprStmt() {
            var expr = ParseExpression();

            if (!Peek.Is(TokenUsage.Assignment)) {
                return WithTrailingWhitespace(new ExpressionStatement {
                    Expression = expr,
                    Span = expr.Span
                });
            }

            if (Peek.Is(TokenKind.Assign)) {
                var targets = new List<Expression>();
                while (TryRead(TokenKind.Assign)) {
                    targets.Add(expr);
                    expr.CheckAssign(this);
                    expr = ParseExpression();
                }

                return WithTrailingWhitespace(new AssignmentStatement {
                    Targets = targets,
                    Expression = expr,
                    Span = new SourceSpan(targets[0].Span.Start, Current.Span.End)
                });
            }

            expr.CheckAugmentedAssign(this);
            return WithTrailingWhitespace(new AugmentedAssignStatement {
                Target = expr,
                Operator = Next().Kind.GetBinaryOperator(),
                Expression = ParseExpression()
            });

        }


        private Expression ParseAssignmentTarget(bool alwaysTuple = false) {
            var start = Peek.Span.Start;
            var targets = new List<SequenceItemExpression>();

            var ws = ReadWhitespace();
            var expr = _features.HasStarUnpacking ? ParseStarName() : ParsePrimaryWithTrailers();
            Debug.Assert(expr.BeforeNode.Length == 0, "Should not have read leading whitespace");
            expr.BeforeNode = ws;

            var ne = expr as NameExpression ?? (expr as StarredExpression)?.Expression as NameExpression;
            if (ne != null) {
                CurrentScope?.AddLocalDefinition(ne.Name);
            }

            if (!Peek.Is(TokenKind.Comma) && !alwaysTuple) {
                return WithTrailingWhitespace(expr);
            }

            while (true) {
                var item = WithTrailingWhitespace(new SequenceItemExpression {
                    Expression = expr,
                    Span = expr.Span,
                    HasComma = TryRead(TokenKind.Comma)
                });

                if (item.IsExpressionEmpty && !item.HasComma) {
                    break;
                }

                targets.Add(item);

                if (!item.HasComma || PeekNonWhitespace.IsAny(TokenUsage.Assignment, TokenUsage.Comparison)) {
                    break;
                }

                ws = ReadWhitespace();
                expr = _features.HasStarUnpacking ? ParseStarName() : ParsePrimaryWithTrailers();
                Debug.Assert(expr.BeforeNode.Length == 0, "Should not have read leading whitespace");
                expr.BeforeNode = ws;

                ne = expr as NameExpression ?? (expr as StarredExpression)?.Expression as NameExpression;
                if (ne != null) {
                    CurrentScope?.AddLocalDefinition(ne.Name);
                }
            }
            var tuple = WithTrailingWhitespace(new TupleExpression {
                Items = targets,
                Span = new SourceSpan(start, Current.Span.End)
            });
            tuple.Freeze();
            return tuple;
        }

        private Expression ParseExpression(
            bool allowIfExpr = true,
            bool allowSlice = false,
            bool allowGenerator = true
        ) {
            var expr = ParseSingleExpression(
                allowIfExpr: allowIfExpr,
                allowSlice: allowSlice,
                allowGenerator: allowGenerator
            );

            if (!Peek.Is(TokenKind.Comma)) {
                return expr;
            }

            var start = expr.Span.Start;
            var tuple = new TupleExpression();
            var item = WithTrailingWhitespace(new SequenceItemExpression {
                Expression = expr,
                Span = expr.Span,
                HasComma = TryRead(TokenKind.Comma)
            });
            tuple.AddItem(item);

            while (true) {
                expr = ParseSingleExpression(
                    allowIfExpr: allowIfExpr,
                    allowSlice: allowSlice,
                    allowGenerator: allowGenerator
                );
                item = new SequenceItemExpression {
                    Expression = expr,
                    Span = expr.Span,
                    HasComma = TryRead(TokenKind.Comma)
                };
                if (item.IsExpressionEmpty && !item.HasComma) {
                    break;
                }
                tuple.AddItem(WithTrailingWhitespace(item));
            }

            tuple.Span = new SourceSpan(start, tuple.Items[tuple.Count - 1].Span.End);
            tuple.Freeze();
            return tuple;
        }

        private Expression ParseSingleExpression(bool allowIfExpr = true, bool allowSlice = false, bool allowGenerator = true) {
            if (PeekNonWhitespace.Is(TokenKind.KeywordLambda)) {
                return ParseLambda();
            }

            var ws = ReadWhitespace();
            Expression expr;
            try {
                expr = ParseComparison();
            } catch (ParseErrorException ex) {
                ReadUntil(IsEndOfStatement);
                ReportError(ex.Message, new SourceSpan(ex.Location, Peek.Span.Start));
                return new ErrorExpression {
                    Span = new SourceSpan(ex.Location, Peek.Span.Start)
                };
            }

            if (allowIfExpr && Peek.Is(TokenKind.KeywordIf)) {
                expr.Freeze();
                var condExpr = new ConditionalExpression { TrueExpression = expr };
                Read(TokenKind.KeywordIf);
                condExpr.Expression = ParseSingleExpression();
                Read(TokenKind.KeywordElse);
                condExpr.FalseExpression = ParseSingleExpression();
                condExpr.Span = new SourceSpan(expr.Span.Start, condExpr.FalseExpression.Span.End);
                expr = WithTrailingWhitespace(condExpr);
            }

            if (allowSlice && Peek.Is(TokenKind.Colon)) {
                expr.Freeze();
                var sliceExpr = new SliceExpression { SliceStart = expr };
                Read(TokenKind.Colon);
                sliceExpr.SliceStop = ParseSingleExpression(allowSlice: false, allowGenerator: allowGenerator);

                if (TryRead(TokenKind.Colon)) {
                    sliceExpr.SliceStep = ParseSingleExpression(allowSlice: false, allowGenerator: allowGenerator);
                }

                sliceExpr.Span = new SourceSpan(expr.Span.Start, Current.Span.End);
                expr = WithTrailingWhitespace(sliceExpr);
            }

            if (allowGenerator && Peek.Is(TokenKind.KeywordFor)) {
                expr.Freeze();
                var genExpr = new GeneratorExpression {
                    Item = expr,
                    Iterators = ReadComprehension()
                };

                genExpr.Span = new SourceSpan(expr.Span.Start, Current.Span.End);
                expr = WithTrailingWhitespace(genExpr);
            }

            expr.BeforeNode = ws;
            expr.Freeze();
            return expr;
        }

        private Expression ParseAsExpressions() {
            var expr = ParseSingleAsExpression();
            if (!Peek.Is(TokenKind.Comma)) {
                return expr;
            }

            var start = expr.Span.Start;
            var tuple = new TupleExpression();
            var item = WithTrailingWhitespace(new SequenceItemExpression {
                Expression = expr,
                Span = expr.Span,
                HasComma = TryRead(TokenKind.Comma)
            });
            tuple.AddItem(item);

            while (!item.IsExpressionEmpty || item.HasComma) {
                expr = ParseSingleAsExpression();
                item = new SequenceItemExpression {
                    Expression = expr,
                    Span = expr.Span,
                    HasComma = TryRead(TokenKind.Comma)
                };
                if (item.IsExpressionEmpty && !item.HasComma) {
                    break;
                }
                tuple.AddItem(WithTrailingWhitespace(item));
            }

            tuple.Span = new SourceSpan(start, expr.Span.End);
            tuple.Freeze();
            return tuple;
        }

        private Expression ParseSingleAsExpression() {
            var expr = ParseSingleExpression();
            if (Peek.Is(TokenKind.KeywordAs)) {
                var start = expr.Span.Start;
                var asExpr = WithTrailingWhitespace(new AsExpression {
                    Expression = expr,
                    Span = Read(TokenKind.KeywordAs),
                    NameExpression = ParseSingleExpression()
                });
                asExpr.Span = new SourceSpan(
                    asExpr.Expression.Span.Start,
                    asExpr.NameExpression.Span.End
                );
                asExpr.Freeze();
                expr = asExpr;
            }
            return expr;
        }

        private Expression ParseLambda() {
            var expr = new LambdaExpression {
                BeforeNode = ReadWhitespace(),
                Span = Read(TokenKind.KeywordLambda),
                Parameters = ParseParameterList(forLambda: true),
                BeforeColon = ReadWhitespace(),
            };

            Read(TokenKind.Colon, errorAt: expr.Span.End);

            var fn = new FunctionDefinition(TokenKind.KeywordLambda);
            _scopes.Push(fn);
            try {
                expr.Expression = ParseSingleExpression();
                expr.IsGenerator = fn.IsGenerator;
            } finally {
                _scopes.Pop();
            }
            return expr;
        }

        private ParameterList ParseParameterList(bool forLambda = false) {
            var parameters = new ParameterList {
                BeforeNode = ReadWhitespace()
            };

            var defaultKind = ParameterKind.Normal;
            int bareStar = -1;

            while (true) {
                if (forLambda && Peek.Is(TokenKind.Colon) ||
                    !forLambda && Peek.Is(TokenKind.RightParenthesis)) {
                    break;
                }

                var start = Peek.Span.Start;
                var p = new Parameter {
                    Kind = defaultKind
                };

                if (TryRead(TokenKind.Multiply)) {
                    p.Kind = ParameterKind.List;
                    defaultKind = ParameterKind.KeywordOnly;
                } else if (TryRead(TokenKind.Power)) {
                    p.Kind = ParameterKind.Dictionary;
                    defaultKind = ParameterKind.KeywordOnly;
                } else if (TryRead(TokenKind.LeftParenthesis)) {
                    p.Kind = ParameterKind.Sublist;
                    var sublist = (TupleExpression)ParseAssignmentTarget(alwaysTuple: true);
                    if (_features.HasSublistParameters) {
                        p.Sublist = sublist;
                    } else {
                        ReportError("sublist parameters are not supported in 3.x", sublist.Span);
                    }
                    Read(TokenKind.RightParenthesis);
                }

                if (p.Kind != ParameterKind.Sublist) {
                    if (_features.HasBareStarParameter && p.Kind == ParameterKind.List && !PeekNonWhitespace.Is(TokenKind.Name)) {
                        if (bareStar >= 0) {
                            ReportError();
                        } else {
                            bareStar = parameters.Count;
                        }
                    } else {
                        p.NameExpression = ReadName();
                    }

                    if (!forLambda && TryRead(TokenKind.Colon)) {
                        p.Annotation = ParseSingleExpression();
                        if (!_features.HasAnnotations) {
                            ReportError("invalid syntax, parameter annotations require 3.x", p.Annotation.Span);
                        }
                    }

                    if (TryRead(TokenKind.Assign)) {
                        p.DefaultValue = ParseSingleExpression();
                    }

                    if (_version.Is2x() && defaultKind == ParameterKind.KeywordOnly &&
                        p.Kind != ParameterKind.List && p.Kind != ParameterKind.Dictionary) {
                        ReportError("positional parameter after * args not allowed", p.NameExpression?.Span ?? p.Span);
                    }
                }

                p.Span = new SourceSpan(start, Current.Span.End);
                p.HasCommaBeforeComment = TryRead(TokenKind.Comma);
                WithTrailingWhitespace(p);
                p.HasCommaAfterNode = TryRead(TokenKind.Comma);
                p.Freeze();

                parameters.AddParameter(p);

                if (!p.HasCommaBeforeComment && !p.HasCommaAfterNode) {
                    break;
                }
            }

            if (_version.Is3x() && bareStar >= 0 && bareStar == parameters.Count - 1) {
                ReportError("named arguments must follow bare *", parameters[bareStar].Span);
            }

            parameters.AfterNode = ReadWhitespace();
            parameters.Freeze();
            return parameters;
        }

        private List<Arg> ParseArgumentList(TokenKind closing, bool allowNames, bool allowSlice = false) {
            var args = new List<Arg>();
            var names = new HashSet<string>();

            while (true) {
                Expression nameExpr = null;
                var expr = ParseSingleExpression(allowSlice: allowSlice);
                var start = expr.Span.Start;

                if (allowNames && TryRead(TokenKind.Assign)) {
                    var name = (expr as NameExpression)?.Name;
                    if (string.IsNullOrEmpty(name)) {
                        ReportError("expected name", expr.Span);
                    } else if (!names.Add(name)) {
                        ReportError("keyword argument repeated", expr.Span);
                    }
                    nameExpr = expr;
                    expr = ParseSingleExpression(allowSlice: allowSlice);
                }

                bool hasComma = TryRead(TokenKind.Comma);
                if (nameExpr == null && Expression.IsNullOrEmpty(expr) && !hasComma) {
                    break;
                }

                var a = WithTrailingWhitespace(new Arg {
                    Expression = expr,
                    NameExpression = nameExpr,
                    HasComma = hasComma
                });


                a.Span = new SourceSpan(start, expr.Span.End);
                a.Freeze();

                if (Expression.IsNullOrEmpty(a.Expression) && !a.HasComma) {
                    break;
                }

                args.Add(a);
            }

            return args;
        }

        private Expression ParseComparison() {
            var expr = ParseNotTest();
            var withinOp = SourceSpan.None;

            var opSpan = Peek.Span;
            var op = PythonOperator.None;
            if (Peek.Is(TokenKind.KeywordAnd)) {
                op = PythonOperator.And;
            } else if (Peek.Is(TokenKind.KeywordOr)) {
                op = PythonOperator.Or;
            } else if (Peek.Is(TokenKind.KeywordNot)) {
                Read(TokenKind.KeywordNot);
                withinOp = ReadWhitespace();
                Read(TokenKind.KeywordIn);
                op = PythonOperator.NotIn;
                opSpan = new SourceSpan(opSpan.Start, Current.Span.End);
            } else if (_version.Is3x() && Peek.Is(TokenKind.LessThanGreaterThan)) {
                ReportError(errorAt: Peek.Span);
                op = PythonOperator.NotEqual;
            } else if (Peek.Is(TokenUsage.Comparison)) {
                op = Peek.Kind.GetBinaryOperator();
            }

            if (op == PythonOperator.None) {
                return expr;
            }

            Next();

            if (op == PythonOperator.Is && PeekNonWhitespace.Is(TokenKind.KeywordNot)) {
                op = PythonOperator.IsNot;
                withinOp = ReadWhitespace();
                Read(TokenKind.KeywordNot);
            }

            var ws = ReadWhitespace();
            var rhs = ParseComparison();
            rhs.BeforeNode = ws;
            rhs.Freeze();

            return WithTrailingWhitespace(new BinaryExpression {
                Left = expr,
                Operator = op,
                OperatorSpan = opSpan,
                WithinOperator = withinOp,
                Right = rhs,
                Span = new SourceSpan(expr.Span.Start, rhs.Span.End)
            });
        }

        private Expression ParseNotTest() {
            if (TryRead(TokenKind.KeywordNot)) {
                var start = Current.Span.Start;
                var ws = ReadWhitespace();
                var expr = ParseNotTest();
                expr.BeforeNode = ws;
                expr.Freeze();
                return WithTrailingWhitespace(new UnaryExpression {
                    Operator = PythonOperator.Not,
                    Expression = expr,
                    Span = new SourceSpan(start, expr.Span.End)
                });
            }
            return ParseStarExpression();
        }

        private Expression ParseStarName() {
            if (TryRead(TokenKind.Multiply) || TryRead(TokenKind.Power)) {
                var start = Current.Span.Start;
                var kind = Current.Kind;
                var ws = ReadWhitespace();
                var expr = ParsePrimaryWithTrailers();
                expr.BeforeNode = ws;
                expr.Freeze();
                return WithTrailingWhitespace(new StarredExpression(kind, expr) {
                    Span = new SourceSpan(start, expr.Span.End)
                });
            }

            return ParsePrimaryWithTrailers();
        }

        private Expression ParseStarExpression() {
            if (TryRead(TokenKind.Multiply) || TryRead(TokenKind.Power)) {
                var start = Current.Span.Start;
                var kind = Current.Kind;
                var ws = ReadWhitespace();
                var expr = ParseExpr();
                expr.BeforeNode = ws;
                expr.Freeze();
                return WithTrailingWhitespace(new StarredExpression(kind, expr) {
                    Span = new SourceSpan(start, expr.Span.End)
                });
            }

            return ParseExpr();
        }

        private Expression ParseExpr(int precedence = 0) {
            var expr = ParseFactor();
            while (Peek.IsAny(TokenUsage.BinaryOperator, TokenUsage.BinaryOrUnaryOperator)) {
                var opSpan = Peek.Span;
                var op = Peek.Kind.GetBinaryOperator();
                if (op == PythonOperator.None) {
                    return expr;
                } else if (op == PythonOperator.MatMultiply && _version < PythonLanguageVersion.V35) {
                    ReportError(errorAt: Next().Span);
                    return expr;
                }

                var prec = op.GetPrecedence();
                if (prec < precedence) {
                    break;
                }

                Next();

                var ws = ReadWhitespace();
                var rhs = ParseExpr(prec);
                rhs.BeforeNode = ws;
                rhs.Freeze();

                expr = WithTrailingWhitespace(new BinaryExpression {
                    Left = expr,
                    Operator = op,
                    OperatorSpan = opSpan,
                    Right = rhs,
                    Span = new SourceSpan(expr.Span.Start, rhs.Span.End)
                });
            }
            return expr;
        }

        private Expression ParseFactor() {
            if (Peek.IsAny(TokenUsage.UnaryOperator, TokenUsage.BinaryOrUnaryOperator)) {
                var token = Next();
                var ws = ReadWhitespace();
                var expr = ParseFactor();
                expr.BeforeNode = ws;
                expr.Freeze();
                var ue = WithTrailingWhitespace(new UnaryExpression {
                    Expression = expr,
                    Span = new SourceSpan(token.Span.Start, Current.Span.End)
                });
                switch (token.Kind) {
                    case TokenKind.Add:
                        ue.Operator = PythonOperator.Pos;
                        break;
                    case TokenKind.Subtract:
                        ue.Operator = PythonOperator.Negate;
                        break;
                    case TokenKind.Twiddle:
                        ue.Operator = PythonOperator.Invert;
                        break;
                    default:
                        ReportError(errorAt: ue.Span);
                        return null;
                }
                return ue;
            }
            return ParseAwaitExpression();
        }

        private Expression ParseAwaitExpression() {
            if (_features.HasAsyncAwait && IsInAsyncFunction && TryRead(TokenKind.KeywordAwait)) {
                var start = Current.Span.Start;
                var ws = ReadWhitespace();
                var expr = ParsePower();
                expr.BeforeNode = ws;
                expr.Freeze();
                return WithTrailingWhitespace(new AwaitExpression {
                    BeforeNode = ws,
                    Expression = expr,
                    Span = new SourceSpan(start, Current.Span.End)
                });
            }
            return ParseYieldOrYieldFromExpression();
        }

        private Expression ParseYieldOrYieldFromExpression() {
            if (TryRead(TokenKind.KeywordYield)) {
                if (PeekNonWhitespace.Is(TokenKind.KeywordFrom)) {
                    return ParseYieldFromExpression();
                } else {
                    return ParseYieldExpression();
                }
            }
            return ParsePower();
        }

        private Expression ParseYieldFromExpression() {
            var start = Current.Span.Start;
            var ws = ReadWhitespace();
            Read(TokenKind.KeywordFrom);

            var func = CurrentScope as FunctionDefinition;
            if (func != null) {
                func.IsGenerator = true;
            }

            if (!_features.HasYieldFrom) {
                ReportError("'yield from' requires 3.3 or later", new SourceSpan(start, Current.Span.End));
            } else if (func == null) {
                ReportError("'yield from' outside of generator", new SourceSpan(start, Current.Span.End));
            } else if (func.IsAsync) {
                ReportError("'yield from' in async function", new SourceSpan(start, Current.Span.End));
            }

            var expr = ParseSingleExpression(allowGenerator: false);
            if (Peek.Is(TokenKind.Comma)) {
                // Cannot yield from a tuple
                ThrowError();
            }
            if (Expression.IsNullOrEmpty(expr)) {
                ReportError(errorAt: new SourceSpan(start, Current.Span.End));
            }

            return WithTrailingWhitespace(new YieldFromExpression {
                BeforeFrom = ws,
                Expression = expr,
                Span = new SourceSpan(start, Current.Span.End)
            });
        }

        private Expression ParseYieldExpression() {
            var start = Current.Span.Start;

            var func = CurrentScope as FunctionDefinition;
            if (func != null) {
                func.IsGenerator = true;
            }

            if (func == null) {
                ReportError("'yield' outside of generator", Current.Span);
            } else if (IsInAsyncFunction) {
                ReportError("'yield' in async function", Current.Span);
            }

            return WithTrailingWhitespace(new YieldExpression {
                Expression = ParseExpression(allowGenerator: false),
                Span = new SourceSpan(start, Current.Span.End)
            });
        }

        private Expression ParsePower() {
            var expr = ParsePrimaryWithTrailers();
            if (TryRead(TokenKind.Power)) {
                var opSpan = Current.Span;
                var ws = ReadWhitespace();
                var rhs = ParseFactor();
                rhs.BeforeNode = ws;
                rhs.Freeze();

                expr = WithTrailingWhitespace(new BinaryExpression {
                    Left = expr,
                    Operator = PythonOperator.Power,
                    OperatorSpan = opSpan,
                    Right = rhs,
                    Span = new SourceSpan(expr.Span.Start, rhs.Span.End)
                });
            }
            return expr;
        }

        private Expression ParsePrimaryWithTrailers() {
            var expr = ParseGroup();
            var start = expr.Span.Start;

            while (true) {
                switch (PeekNonWhitespace.Kind) {
                    case TokenKind.Comment:
                        expr = WithTrailingWhitespace(expr);
                        break;
                    case TokenKind.Dot:
                        expr.AfterNode = ReadWhitespace();
                        expr.Freeze();
                        Read(TokenKind.Dot);
                        expr = new MemberExpression {
                            Expression = expr,
                            NameExpression = ReadMemberName()
                        };
                        expr.Span = new SourceSpan(start, Current.Span.End);
                        break;
                    case TokenKind.LeftParenthesis:
                        expr.AfterNode = ReadWhitespace();
                        expr.Freeze();
                        Read(TokenKind.LeftParenthesis);
                        expr = new CallExpression {
                            Expression = expr,
                            Args = ParseArgumentList(TokenKind.RightParenthesis, true)
                        };
                        expr.Span = new SourceSpan(start, Read(TokenKind.RightParenthesis).End);
                        break;
                    case TokenKind.LeftBracket:
                        expr.AfterNode = ReadWhitespace();
                        expr.Freeze();
                        Read(TokenKind.LeftBracket);
                        expr = new IndexExpression {
                            Expression = expr,
                            Index = ParseExpression(allowSlice: true)
                        };
                        expr.Span = new SourceSpan(start, Read(TokenKind.RightBracket).End);
                        break;
                    default:
                        return WithTrailingWhitespace(expr);
                }
            }
        }

        private Expression ParseGroup() {
            if (!Peek.Is(TokenUsage.BeginGroup)) {
                return ParsePrimary();
            }

            var start = Peek.Span.Start;

            switch (Peek.Kind) {
                case TokenKind.LeftBrace:
                    return ParseDictOrSetLiteralOrComprehension();
                case TokenKind.LeftBracket:
                    return ParseListLiteralOrComprehension();
                case TokenKind.LeftParenthesis:
                    Next();
                    return WithTrailingWhitespace(new ParenthesisExpression {
                        Expression = ParseExpression(),
                        Span = new SourceSpan(start, Read(TokenKind.RightParenthesis).End)
                    });

                case TokenKind.LeftSingleQuote:
                case TokenKind.LeftDoubleQuote:
                case TokenKind.LeftSingleTripleQuote:
                case TokenKind.LeftDoubleTripleQuote:
                    return ParseStringLiteral();

                case TokenKind.LeftBackQuote:
                    return ParseReprLiteral();

                default:
                    Debug.Fail("Unhandled group: " + Peek.Kind);
                    return new ErrorExpression {
                        Span = Peek.Span
                    };
            }
        }

        private Expression ParsePrimary() {
            if (!Peek.Is(TokenUsage.Primary)) {
                if (!IsInAsyncFunction && Peek.IsAny(TokenKind.KeywordAsync, TokenKind.KeywordAwait)) {
                    // pass
                } else if (!_features.HasWith && Peek.Is(TokenKind.KeywordWith)) {
                    // pass
                } else if (!_features.HasAs && Peek.Is(TokenKind.KeywordAs)) {
                    // pass
                } else if (_features.HasPrintFunction && Peek.Is(TokenKind.KeywordPrint)) {
                    // pass
                } else if (!_features.HasNonlocal && Peek.Is(TokenKind.KeywordNonlocal)) {
                    // pass
                } else if (!_features.HasExecStatement && Peek.Is(TokenKind.KeywordExec)) {
                    // pass
                } else if (Peek.IsAny(TokenUsage.Assignment, TokenUsage.EndGroup, TokenUsage.EndStatement) ||
                    Peek.Is(TokenCategory.Delimiter)
                ) {
                    return new EmptyExpression {
                        Span = new SourceSpan(Peek.Span.Start, Peek.Span.Start)
                    };
                } else {
                    ThrowError();
                    return null;
                }
            }

            object value;
            string text;

            switch (Peek.Kind) {
                case TokenKind.Name:
                // If we've maked it this far, these keywords should be treated
                // as regular names.
                case TokenKind.KeywordAsync:
                case TokenKind.KeywordAwait:
                case TokenKind.KeywordWith:
                case TokenKind.KeywordAs:
                case TokenKind.KeywordNonlocal:
                case TokenKind.KeywordExec:
                    text = _tokenization.GetTokenText(Next());
                    return WithTrailingWhitespace(new NameExpression(text) {
                        Span = Current.Span
                    });

                case TokenKind.KeywordPrint:
                    var printToken = Next();
                    if (PeekNonWhitespace.Is(TokenUsage.EndStatement) ||
                        !PeekNonWhitespace.IsAny(TokenCategory.StringLiteral, TokenCategory.Identifier, TokenCategory.NumericLiteral)) {
                        return WithTrailingWhitespace(new NameExpression("print") {
                            Span = printToken.Span
                        });
                    } else {
                        var ws = ReadWhitespace();
                        var start = Peek.Span.Start;
                        ReadUntil(t => t.Is(TokenUsage.EndStatement));
                        ReportError(
                            "Missing parentheses in call to 'print'",
                            new SourceSpan(start, Current.Span.End)
                        );
                        return new ErrorExpression {
                            BeforeNode = ws,
                            Span = new SourceSpan(printToken.Span.Start, Current.Span.End)
                        };
                    }

                case TokenKind.KeywordNone:
                    return WithTrailingWhitespace(new ConstantExpression {
                        Span = Next().Span,
                        Value = null
                    });

                case TokenKind.KeywordTrue:
                    if (_features.HasConstantBooleans) {
                        return WithTrailingWhitespace(new ConstantExpression {
                            Span = Next().Span,
                            Value = true
                        });
                    } else {
                        return WithTrailingWhitespace(new NameExpression("True") { Span = Next().Span });
                    }
                case TokenKind.KeywordFalse:
                    if (_features.HasConstantBooleans) {
                        return WithTrailingWhitespace(new ConstantExpression {
                            Span = Next().Span,
                            Value = false
                        });
                    } else {
                        return WithTrailingWhitespace(new NameExpression("False") { Span = Next().Span });
                    }

                case TokenKind.LiteralDecimal:
                case TokenKind.LiteralDecimalLong:
                case TokenKind.LiteralHex:
                case TokenKind.LiteralHexLong:
                case TokenKind.LiteralOctal:
                case TokenKind.LiteralOctalLong:
                case TokenKind.LiteralBinary:
                case TokenKind.LiteralFloat:
                case TokenKind.LiteralImaginary:
                    text = _tokenization.GetTokenText(Next());
                    if (!TryParseNumber(text, Current.Kind, out value)) {
                        ReportError("invalid number", Current.Span);
                        return WithTrailingWhitespace(new ErrorExpression {
                            Span = Current.Span
                        }); 
                    }
                    return WithTrailingWhitespace(new ConstantExpression {
                        Span = Current.Span,
                        Value = value
                    });

                case TokenKind.Ellipsis:
                    if (_version.Is2x()) {
                        ReportError("unexpected token '.'");
                    }
                    return WithTrailingWhitespace(new ConstantExpression {
                        Span = Next().Span,
                        Value = Ellipsis.Value
                    });

                default:
                    return WithTrailingWhitespace(new ErrorExpression {
                        Span = Peek.Span
                    });
            }
        }

        private bool TryParseNumber(string text, TokenKind kind, out object value) {
            value = null;

            bool reduceType = true;
            switch (kind) {
                case TokenKind.LiteralDecimalLong:
                case TokenKind.LiteralHexLong:
                case TokenKind.LiteralOctalLong:
                case TokenKind.LiteralImaginary:
                    text = text.Remove(text.Length - 1);
                    reduceType = false;
                    break;
            }

            BigInteger bi = 0;
            double d = 0;

            switch (kind) {
                case TokenKind.LiteralDecimal:
                case TokenKind.LiteralDecimalLong:
                    if (!ReadDecimal(text, out bi)) {
                        return false;
                    }
                    break;

                case TokenKind.LiteralOctal:
                case TokenKind.LiteralOctalLong:
                    if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) {
                        if (_version < PythonLanguageVersion.V26) {
                            ReportError(errorAt: Current.Span);
                        }
                        text = text.Substring(2);
                    }
                    if (!ReadOctal(text, out bi)) {
                        return false;
                    }
                    break;

                case TokenKind.LiteralHex:
                case TokenKind.LiteralHexLong:
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                        text = text.Substring(2);
                    }
                    if (!ReadHex(text, out bi)) {
                        return false;
                    }
                    break;

                case TokenKind.LiteralBinary:
                    if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) {
                        if (_version < PythonLanguageVersion.V26) {
                            ReportError(errorAt: Current.Span);
                        }
                        text = text.Substring(2);
                    }
                    if (!ReadBinary(text, out bi)) {
                        return false;
                    }
                    break;

                case TokenKind.LiteralFloat:
                case TokenKind.LiteralImaginary:
                    try {
                        d = double.Parse(text);
                    } catch (FormatException) {
                        return false;
                    } catch (OverflowException) {
                        d = double.PositiveInfinity;
                    }
                    value = (kind == TokenKind.LiteralFloat) ? d : (object)new Complex(0.0, d);
                    return true;
                    
                default:
                    return false;
            }

            value = bi;

            if (reduceType && int.MinValue <= bi && bi <= int.MaxValue) {
                value = (int)bi;
            }

            return true;
        }

        private Expression ParseStringLiteral() {
            var parts = new List<Expression>();
            var start = Peek.Span.Start;

            while (Peek.Is(TokenUsage.BeginGroup) && Peek.Is(TokenCategory.StringLiteral)) {
                var opening = Next();
                var openingText = _tokenization.GetTokenText(opening).ToLowerInvariant();
                bool isRaw = openingText.Contains('r');
                bool isBytes = openingText.Contains('b');
                bool isUnicode = openingText.Contains('u');
                // TODO: Implement formatted string handling
                //bool isFormatted = text.Contains('f');

                if (openingText.Any(c => c != 'r' && c != 'b' && c != 'u' && c != '\'' && c != '"')) {
                    ReportError("invalid string prefix", opening.Span);
                }
                if (isUnicode) {
                    if (!_features.HasUnicodePrefix) {
                        ReportError("invalid syntax", opening.Span);
                    } else if (isBytes) {
                        ReportError("b and u prefixes are not compatible", opening.Span);
                    } else if (_version.Is3x() && isRaw) {
                        ReportError("r and u prefixes are not compatible", opening.Span);
                    }
                }
                if (isBytes) {
                    if (!_features.HasBytesPrefix) {
                        ReportError("invalid syntax", opening.Span);
                    } else if (isRaw && !_features.HasRawBytesPrefix) {
                        ReportError("invalid syntax", opening.Span);
                    }
                }
                if (!isBytes && !isUnicode) {
                    isUnicode = _features.HasUnicodeLiterals;
                    isBytes = !isUnicode;
                }

                var closing = opening.Kind.GetGroupEnding();
                var quoteStart = Current.Span.Start;

                var fullText = new StringBuilder();
                while (TryRead(TokenKind.LiteralString)) {
                    var text = _tokenization.GetTokenText(Current);
                    // Exclude newline if escaped
                    int lastEscape = text.LastIndexOf('\\');
                    if ((lastEscape > 0 && text[lastEscape - 1] != '\\' || lastEscape == 0) &&
                        lastEscape + 1 < text.Length) {
                        var tail = text.Substring(lastEscape + 1);
                        if (tail == "\r\n" || tail == "\r" || tail == "\n") {
                            text = text.Remove(text.Length - (tail.Length + 1));
                        }
                    }
                    if (!isRaw) {
                        text = ParseStringEscapes(text, Current.Span.Start, isUnicode);
                    }
                    fullText.Append(text);
                }
                Read(closing);

                var expr = WithTrailingWhitespace(new ConstantExpression {
                    Value = isBytes ?
                        new ByteString(_tokenization.Encoding.GetBytes(fullText.ToString()), fullText.ToString()) :
                        (object)fullText.ToString(),
                    Span = new SourceSpan(quoteStart, Current.Span.End)
                });
                expr.Freeze();

                parts.Add(expr);
            }

            return WithTrailingWhitespace(new StringExpression {
                Parts = parts,
                Span = new SourceSpan(start, Current.Span.End)
            });
        }

        private string ParseStringEscapes(string str, SourceLocation strStart, bool isUnicode) {
            int prev = 0;
            int i;
            var res = new StringBuilder();

            while (prev < str.Length && (i = str.IndexOf('\\', prev)) >= 0) {
                if (i > prev) {
                    res.Append(str.Substring(prev, i - prev - 1));
                }

                if (i + 1 >= str.Length) {
                    Debug.Fail("Should never have trailing backslash in string");
                    res.Append('\\');
                    break;
                }

                char c = str[i + 1];
                switch (c) {
                    case '\\': res.Append('\\'); i += 2; break;
                    case '\'': res.Append('\''); i += 2; break;
                    case '"': res.Append('"'); i += 2; break;
                    case 'a': res.Append('\a'); i += 2; break;
                    case 'b': res.Append('\b'); i += 2; break;
                    case 'f': res.Append('\f'); i += 2; break;
                    case 'n': res.Append('\n'); i += 2; break;
                    case 'r': res.Append('\r'); i += 2; break;
                    case 't': res.Append('\t'); i += 2; break;
                    case 'v': res.Append('\v'); i += 2; break;
                    case 'x': res.Append(ReadEscape(str, strStart, i, "\\x00", isUnicode)); i += 4; break;
                    case 'u':
                        if (!isUnicode) {
                            goto default;
                        }
                        res.Append(ReadEscape(str, strStart, i, "\\uxxxx", isUnicode));
                        i += 6;
                        break;
                    case 'U':
                        if (!isUnicode) {
                            goto default;
                        }
                        res.Append(ReadEscape(str, strStart, i, "\\Uxxxxxxxx", isUnicode));
                        i += 10;
                        break;
                    default:
                        if (CharUnicodeInfo.GetDigitValue(c) >= 0 && CharUnicodeInfo.GetDigitValue(c) <= 7) {
                            var fmt = "\\o";
                            while (fmt.Length < 4 && i + fmt.Length < str.Length) {
                                int d = CharUnicodeInfo.GetDigitValue(str, i + fmt.Length);
                                if (d < 0 || d > 7) {
                                    break;
                                }
                                fmt += 'o';
                            }
                            res.Append(ReadEscape(str, strStart, i, fmt, isUnicode));
                            i += fmt.Length;
                        } else {
                            res.Append("\\");
                            res.Append(c);
                            i += 2;
                        }
                        break;
                }

                prev = i;
            }

            if (prev < str.Length) {
                if (prev == 0) {
                    res.Append(str);
                } else {
                    res.Append(str.Substring(prev));
                }
            }

            return res.ToString();
        }

        private string ReadEscape(string str, SourceLocation strStart, int i, string format, bool isUnicode) {
            if (i + format.Length > str.Length) {
                ReportError("truncated " + format + " escape", new SourceSpan(strStart + i, str.Length - i));
                return str.Substring(i);
            }

            var span = new SourceSpan(strStart + i, format.Length);
            var text = str.Substring(i, format.Length);
            BigInteger bi;
            switch (format[1]) {
                case 'x':
                    if (!ReadHex(text.Substring(2), out bi) || bi > byte.MaxValue) {
                        ReportError("invalid " + format + " escape", span);
                        return text;
                    }
                    return new string((char)(byte)bi, 1);
                case 'o':
                    if (!ReadOctal(text.Substring(1), out bi) || bi > byte.MaxValue) {
                        ReportError("invalid " + format + " escape", span);
                        return text;
                    }
                    return new string((char)(byte)bi, 1);
                case 'u':
                    if (!isUnicode || !ReadHex(text.Substring(2), out bi) || bi > ushort.MaxValue) {
                        ReportError("invalid " + format + " escape", span);
                        return text;
                    }
                    return new string((char)(ushort)bi, 1);
                case 'U':
                    if (!isUnicode || !ReadHex(text.Substring(2), out bi) || bi > ulong.MaxValue) {
                        ReportError("invalid " + format + " escape", span);
                        return text;
                    }
                    return Encoding.UTF32.GetString(BitConverter.GetBytes((ulong)bi));
            }

            return text;
        }

        private Expression ParseReprLiteral() {
            var start = Read(TokenKind.LeftBackQuote).Start;

            var expr = new BackQuoteExpression {
                Expression = ParseExpression(),
                Span = new SourceSpan(start, Read(TokenKind.RightBackQuote).End)
            };

            if (!_features.HasReprLiterals) {
                ReportError("invalid syntax", expr.Span);
            }

            return expr;
        }

        private Expression ParseListLiteralOrComprehension() {
            var start = Read(TokenKind.LeftBracket).Start;

            if (PeekNonWhitespace.Is(TokenKind.RightBracket)) {
                return WithTrailingWhitespace(new ListExpression {
                    BeforeNode = ReadWhitespace(),
                    Span = new SourceSpan(start, Read(TokenKind.RightBracket).End)
                });
            }

            var expr = ParseSingleExpression(allowSlice: false, allowGenerator: false);
            if (PeekNonWhitespace.Is(TokenKind.KeywordFor)) {
                return WithTrailingWhitespace(new ListComprehension {
                    Item = expr,
                    Iterators = ReadComprehension(),
                    Span = new SourceSpan(start, Read(TokenKind.RightBracket).End)
                });
            }

            var list = new ListExpression();
            while (true) {
                var item = new SequenceItemExpression {
                    Expression = expr,
                    Span = expr.Span,
                    HasComma = TryRead(TokenKind.Comma)
                };
                if (item.IsExpressionEmpty && !item.HasComma) {
                    break;
                }
                list.AddItem(WithTrailingWhitespace(item));
                expr = ParseSingleExpression(allowGenerator: false, allowSlice: true);
            }

            Read(TokenKind.RightBracket);
            list.Span = new SourceSpan(start, Current.Span.End);
            return WithTrailingWhitespace(list);
        }

        private Expression ParseDictOrSetLiteralOrComprehension() {
            var start = Read(TokenKind.LeftBrace).Start;

            if (PeekNonWhitespace.Is(TokenKind.RightBrace)) {
                return WithTrailingWhitespace(new DictionaryExpression {
                    BeforeNode = ReadWhitespace(),
                    Span = new SourceSpan(start, Read(TokenKind.RightBrace).End)
                });
            }

            var expr = ParseSingleExpression(allowSlice: true, allowGenerator: false);
            if (expr is SliceExpression) {
                expr = ParseDictLiteralOrComprehension(expr);
            } else {
                expr = ParseSetLiteralOrComprehension(expr);
            }

            Read(TokenKind.RightBrace);
            expr.Span = new SourceSpan(start, Current.Span.End);
            return WithTrailingWhitespace(expr);
        }

        private Expression ParseDictLiteralOrComprehension(Expression expr) {
            var sliceExpr = (SliceExpression)expr;

            if (PeekNonWhitespace.Is(TokenKind.KeywordFor)) {
                if (sliceExpr.StepProvided) {
                    ReportError(errorAt: sliceExpr.SliceStep.Span);
                }
                var comp = new DictionaryComprehension {
                    Key = sliceExpr.SliceStart,
                    Value = sliceExpr.SliceStop,
                    Iterators = ReadComprehension()
                };
                if (!_features.HasDictComprehensions) {
                    ReportError(
                        "invalid syntax, dictionary comprehensions require Python 2.7 or later",
                        errorAt: new SourceSpan(sliceExpr.Span.Start, Current.Span.End)
                    );
                }
                return comp;
            }

            var dict = new DictionaryExpression();

            while (true) {
                var item = new SequenceItemExpression {
                    Expression = expr,
                    HasComma = TryRead(TokenKind.Comma)
                };
                if (item.IsExpressionEmpty && !item.HasComma) {
                    break;
                }
                dict.AddItem(WithTrailingWhitespace(item));

                var prevStart = expr.Span.Start;
                expr = ParseSingleExpression(allowGenerator: false, allowSlice: true);
                sliceExpr = expr as SliceExpression;
                if (sliceExpr == null && !(expr is EmptyExpression)) {
                    ReportError(errorAt: expr.Span);
                }
            }

            // Don't read the closing brace or comments here
            return dict;
        }

        private Expression ParseSetLiteralOrComprehension(Expression expr) {
            if (PeekNonWhitespace.Is(TokenKind.KeywordFor)) {
                var comp = new SetComprehension {
                    Item = expr,
                    Iterators = ReadComprehension()
                };
                if (!_features.HasSetLiterals) {
                    ReportError(
                        "invalid syntax, set literals require Python 2.7 or later",
                        new SourceSpan(expr.Span.Start, Current.Span.End)
                    );
                }
                return comp;
            }

            var set = new SetExpression();
            var start = expr.Span.Start;

            while (true) {
                var item = new SequenceItemExpression {
                    Expression = expr,
                    HasComma = TryRead(TokenKind.Comma)
                };
                if (item.IsExpressionEmpty && !item.HasComma) {
                    break;
                }
                set.AddItem(WithTrailingWhitespace(item));
                expr = ParseSingleExpression(allowGenerator: false, allowSlice: true);
            }

            if (Peek.Is(TokenKind.RightBrace) || Peek.Is(TokenUsage.EndStatement)) {
                if (!_features.HasSetLiterals) {
                    ReportError(
                        "invalid syntax, set literals require Python 2.7 or later",
                        new SourceSpan(start, Current.Span.End)
                    );
                }
            }

            // Don't read the closing brace or comments here
            return set;
        }

        private List<ComprehensionIterator> ReadComprehension() {
            var iterators = new List<ComprehensionIterator>();
            while (PeekNonWhitespace.IsAny(TokenKind.KeywordFor, TokenKind.KeywordIf)) {
                var ws = ReadWhitespace();

                ComprehensionIterator it = null;
                var start = Peek.Span.Start;

                if (TryRead(TokenKind.KeywordFor)) {
                    it = new ComprehensionFor {
                        Left = ParseAssignmentTarget()
                    };
                    Read(TokenKind.KeywordIn);
                    if (_features.HasTupleAsComprehensionTarget) {
                        ((ComprehensionFor)it).List = ParseExpression(allowIfExpr: false, allowGenerator: false);
                    } else {
                        ((ComprehensionFor)it).List = ParseSingleExpression(allowIfExpr: false, allowGenerator: false);
                    }
                } else if (TryRead(TokenKind.KeywordIf)) {
                    it = new ComprehensionIf {
                        Test = ParseSingleExpression(allowIfExpr: false, allowGenerator: false)
                    };
                }

                it.BeforeNode = ws;
                it.AfterNode = ReadWhitespace();
                it.Span = new SourceSpan(start, Current.Span.End);
                iterators.Add(it);
            }
            return iterators;
        }

        private void ReadCompoundStatement(CompoundStatement stmt) {
            var start = stmt.Span.IsValid ? stmt.Span.Start : Current.Span.Start;

            stmt.BeforeColon = ReadWhitespace();
            Read(TokenKind.Colon);
            stmt.AfterColon = ReadWhitespace();
            stmt.Body = ParseSuite();
            stmt.Span = new SourceSpan(start, Current.Span.End);
        }

        #endregion

        #region Read functions

        private SourceSpan ReadWhitespace() {
            SourceLocation start = Peek.Span.Start, end = Peek.Span.Start;

            while (Peek.IsAny(TokenKind.Whitespace, TokenKind.ExplicitLineJoin, TokenKind.Comment)) {
                end = Next().Span.End;
            }

            return new SourceSpan(start, end);
        }

        //private void MaybeReadComment(Node node) {
        //    if (PeekNonWhitespace.Is(TokenKind.Comment)) {
        //        Debug.Assert(node.CommentAfter == null, "Already read one comment");
        //        node.CommentAfter = ReadComment(allowEmptyComment: false);
        //    }
        //    MaybeReadWhitespaceAfterNode(node);
        //}


        private void MaybeReadWhitespaceAfterNode(Node node) {
            var ws = ReadWhitespace();
            Debug.Assert(node.AfterNode.Length == 0 || ws.Length == 0, "Already read whitespace");
            if (ws.Length > 0 || node.AfterNode.End.Index < ws.End.Index) {
                node.AfterNode = ws;
            }
        }

        private SourceSpan ReadNewLine() {
            if (Peek.Is(TokenKind.NewLine)) {
                return Next().Span;
            }
            return SourceSpan.None;
        }

        private T WithTrailingWhitespace<T>(T target) where T : Node {
            var ws = ReadWhitespace();
            if (ws.Length > target.AfterNode.Length) {
                target.AfterNode = ws;
            }
            return target;
        }

        private NameExpression ReadMemberName(string error = "invalid syntax", SourceLocation? errorAt = null) {
            var prefix = CurrentClass?.Name;
            if (!string.IsNullOrEmpty(prefix)) {
                prefix = "_" + prefix.TrimStart('_');
            }
            return ReadName(prefix, error: error, errorAt: errorAt);
        }

        private Expression ReadNameOrDottedName() {
            var name = ReadDottedName();
            if ((name.Names?.Count ?? 0) == 1) {
                return name.Names[0];
            }
            return name;
        }

        private DottedName ReadDottedName() {
            var names = new List<NameExpression>();
            var beforeNode = ReadWhitespace();
            while (true) {
                var p = PeekNonWhitespace;
                if (p.Is(TokenKind.Ellipsis)) {
                    var ws = ReadWhitespace();
                    var start = Next().Span.Start;
                    var ne = NameExpression.Empty(start, ws);
                    ne.Freeze();
                    names.Add(ne);
                    ne = NameExpression.Empty(start + 1);
                    ne.Freeze();
                    names.Add(ne);
                    ne = WithTrailingWhitespace(NameExpression.Empty(start + 2));
                    ne.Freeze();
                    names.Add(ne);
                    continue;
                }

                if (p.Is(TokenKind.Dot)) {
                    var ws = ReadWhitespace();
                    var start = Next().Span.Start;
                    var ne = WithTrailingWhitespace(NameExpression.Empty(start, ws));
                    ne.Freeze();
                    names.Add(ne);
                    continue;
                }

                var n = TryReadName();
                if (n == null) {
                    break;
                }
                names.Add(n);
                if (!TryRead(TokenKind.Dot)) {
                    break;
                }
            }
            return WithTrailingWhitespace(new DottedName {
                BeforeNode = beforeNode,
                Names = names,
                Span = new SourceSpan((names.ElementAtOrDefault(0)?.Span.Start ?? beforeNode.End), Current.Span.End)
            });
        }

        private NameExpression TryReadName(string prefix = null, bool allowStar = false) {
            string name;
            var p = PeekNonWhitespace;
            switch (p.Kind) {
                case TokenKind.Name:
                    name = _tokenization.GetTokenText(p);
                    break;
                case TokenKind.KeywordAsync:
                case TokenKind.KeywordAwait:
                    if (_features.HasAsyncAwait && IsInAsyncFunction) {
                        return null;
                    }
                    name = p.Is(TokenKind.KeywordAsync) ? "async" : "await";
                    break;
                case TokenKind.KeywordWith:
                    if (_features.HasWith) {
                        return null;
                    }
                    name = "with";
                    break;
                case TokenKind.KeywordAs:
                    if (_features.HasAs) {
                        return null;
                    }
                    name = "as";
                    break;
                case TokenKind.KeywordNonlocal:
                    if (_features.HasNonlocal) {
                        return null;
                    }
                    name = "nonlocal";
                    break;
                case TokenKind.KeywordExec:
                    name = "exec";
                    break;
                case TokenKind.Multiply:
                    if (!allowStar) {
                        return null;
                    }
                    name = "*";
                    break;
                default:
                    return null;
            }

            if (string.IsNullOrEmpty(name) || !name.StartsWith("__") || name.EndsWith("__")) {
                prefix = null;
            }

            var expr = WithTrailingWhitespace(new NameExpression(name, prefix) {
                BeforeNode = ReadWhitespace(),
                Span = Next().Span,
            });

            expr.Freeze();
            return expr;
        }

        private NameExpression ReadName(
            string prefix = null,
            bool allowStar = false,
            string error = "invalid syntax",
            SourceLocation? errorAt = null
        ) {
            var n = TryReadName(prefix, allowStar);
            if (n == null) {
                ThrowError(error, errorAt);
            }
            return n;
        }

        private bool TryRead(TokenKind kind) {
            if (Peek.Is(kind)) {
                Next();
                return true;
            }
            return false;
        }

        private SourceSpan Read(TokenKind kind, string error = "invalid syntax", SourceLocation? errorAt = null) {
            if (!TryRead(kind)) {
                ThrowError(error, errorAt);
            }
            return Current.Span;
        }

        private void ReadUntil(Func<Token, bool> predicate) {
            while (!Peek.Is(TokenKind.EndOfFile) && !predicate(Peek)) {
                Next();
            }
        }

        private void CheckCurrent(TokenKind kind, string error = "invalid syntax", SourceLocation? errorAt = null) {
            if (Current.Kind != kind) {
                ThrowError(error, errorAt);
            }
        }

        #endregion

        #region Token Predicates

        private static bool IsEndOfStatement(Token token) {
            return token.Is(TokenUsage.EndStatement);
        }

        #endregion

        #region String Conversions

        private static bool ReadDecimal(string str, out BigInteger value) {
            return BigInteger.TryParse(
                str,
                NumberStyles.None,
                NumberFormatInfo.InvariantInfo,
                out value
            );
        }

        private static bool ReadHex(string str, out BigInteger value) {
            return BigInteger.TryParse(
                "0" + str,
                NumberStyles.AllowHexSpecifier,
                NumberFormatInfo.InvariantInfo,
                out value
            );
        }

        private static bool ReadOctal(string str, out BigInteger value) {
            value = 0;
            for (int i = 0; i < str.Length; ++i) {
                int v = CharUnicodeInfo.GetDigitValue(str, i);
                if (v < 0 || v > 7) {
                    return false;
                }
                value = value * 8 + v;
            }
            return true;
        }

        private static bool ReadBinary(string str, out BigInteger value) {
            value = 0;
            for (int i = 0; i < str.Length; ++i) {
                int v = CharUnicodeInfo.GetDigitValue(str, i);
                if (v < 0 || v > 1) {
                    return false;
                }
                value = value * 2 + v;
            }
            return true;
        }

        #endregion

        #region Token Scanning

        private Token Current {
            get {
                return _current;
            }
        }

        private void FillLookahead(int tokenCount) {
            if (_lookahead == null) {
                return;
            }

            while (_lookahead.Count < tokenCount) {
                if (_tokenEnumerator.MoveNext()) {
                    if (_tokenEnumerator.Current.Is(TokenKind.EndOfFile)) {
                        _eofToken = _tokenEnumerator.Current;
                    }
                    _lookahead.Add(_tokenEnumerator.Current);
                } else {
                    _lookahead.Add(_eofToken);
                }
            }
        }

        private void PushCurrent(Token token) {
            if (_lookahead != null) {
                _lookahead.Insert(0, _current);
            } else {
                _lookahead = new List<Token> { _current };
            }
            _current = token;
        }

        private Token Next() {
            if (_lookahead == null) {
                return _eofToken;
            }
            FillLookahead(1);
            _current = _lookahead[0];
            _lookahead.RemoveAt(0);
            if (_current.Equals(_eofToken) && _lookahead.Count == 0) {
                _lookahead = null;
            }
            return _current;
        }

        private Token PeekAhead(int count = 1) {
            if (_lookahead == null) {
                return _eofToken;
            }
            FillLookahead(count);
            return _lookahead[count - 1];
        }

        private Token Peek => PeekAhead();

        private Token PeekNonWhitespace {
            get {
                if (_lookahead == null) {
                    return _eofToken;
                }
                FillLookahead(2);
                int i = 0;
                while (true) {
                    if (_lookahead[i].Is(TokenKind.EndOfFile)) {
                        return _lookahead[i];
                    } else if (_lookahead[i].IsAny(TokenKind.Whitespace, TokenKind.ExplicitLineJoin)) {
                        i += 1;
                    } else {
                        return _lookahead[i];
                    }
                    FillLookahead(i + 2);
                }
            }
        }

        #endregion

        #region Error Handling

        internal void ReportError(string error = "invalid syntax", SourceSpan? errorAt = null) {
            _errors.Add(error, errorAt ?? Peek.Span, 0, Severity.Error);
        }

        internal void ReportWarning(string error = "invalid syntax", SourceSpan? errorAt = null) {
            _errors.Add(error, errorAt ?? Peek.Span, 0, Severity.Warning);
        }

        internal void ThrowError(string error = "invalid syntax", SourceLocation? errorAt = null) {
            throw new ParseErrorException(error, errorAt ?? Peek.Span.Start);
        }

        #endregion
    }
}
