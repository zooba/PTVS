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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Parsing {
    public class Parser {
        private readonly Tokenization _tokenization;
        private readonly PythonLanguageVersion _version;
        private FutureOptions _future;

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

        public Parser(Tokenization tokenization) {
            _tokenization = tokenization;
            _version = _tokenization.LanguageVersion;
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

        internal bool HasAnnotations => _version >= PythonLanguageVersion.V30;
        internal bool HasAs => _version >= PythonLanguageVersion.V26 || _future.HasFlag(FutureOptions.WithStatement);
        internal bool HasAsyncAwait => _version >= PythonLanguageVersion.V35;
        internal bool HasBareStarParameter => _version.Is3x();
        internal bool HasBytesPrefix => _version >= PythonLanguageVersion.V26;
        internal bool HasClassDecorators => _version >= PythonLanguageVersion.V26;
        internal bool HasConstantBooleans => _version.Is3x();
        internal bool HasDictComprehensions => _version >= PythonLanguageVersion.V27;
        internal bool HasExecStatement => _version.Is2x();
        internal bool HasGeneralUnpacking => _version >= PythonLanguageVersion.V35;
        internal bool HasGeneratorReturn => _version >= PythonLanguageVersion.V33;
        internal bool HasNonlocal => _version.Is3x();
        internal bool HasPrintFunction => _version.Is3x() || _future.HasFlag(FutureOptions.PrintFunction);
        internal bool HasRawBytesPrefix => _version >= PythonLanguageVersion.V33;
        internal bool HasReprLiterals => _version.Is2x();
        internal bool HasSetLiterals => _version >= PythonLanguageVersion.V27;
        internal bool HasStarUnpacking => _version.Is3x();
        internal bool HasSublistParameters => _version.Is2x();
        internal bool HasTrueDivision => _version.Is3x() || _future.HasFlag(FutureOptions.TrueDivision);
        internal bool HasTupleAsComprehensionTarget => _version.Is2x();
        internal bool HasUnicodeLiterals => _version.Is3x() || _future.HasFlag(FutureOptions.UnicodeLiterals);
        internal bool HasUnicodePrefix => _version < PythonLanguageVersion.V30 || _version >= PythonLanguageVersion.V33;
        internal bool HasWith => _version >= PythonLanguageVersion.V26 || _future.HasFlag(FutureOptions.WithStatement);
        internal bool HasYieldFrom => _version >= PythonLanguageVersion.V33;

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
                return new PythonAst(ParseSuite(assumeMultiLine: true), _tokenization);
            } finally {
                _errors = null;
            }
        }

        private SuiteStatement ParseSuite(bool assumeMultiLine = false) {
            var body = new List<Statement>();

            var prevIndent = _currentIndent;
            var prevIndentLength = _currentIndentLength;
            var prevSingleLine = _singleLine;

            _singleLine = !assumeMultiLine && !TryRead(TokenKind.NewLine);
            var indent = TryRead(TokenKind.SignificantWhitespace) ? Current.Span : SourceSpan.None;
            _currentIndent = _tokenization.GetTokenText(indent);
            _currentIndentLength = GetIndentLength(_currentIndent);

            // Leading whitespace is already read; it will be attached to the
            // suite rather than each statement.
            var stmt = ParseStmt();

            while (IsCurrentStatementBreak()) {
                stmt.AfterNode = ReadCurrentStatementBreak(stmt is CompoundStatement);
                stmt.Freeze();

                body.Add(stmt);

                stmt = ParseStmt();
            }

            stmt.Freeze();
            body.Add(stmt);

            _currentIndent = prevIndent;
            _currentIndentLength = prevIndentLength;
            _singleLine = prevSingleLine;

            var suite = new SuiteStatement(body, indent);
            suite.Comment = ReadComment();
            suite.Freeze();

            return suite;
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
            while (!(p2 = PeekAhead(++lookaheadCount)).IsAny(TokenKind.SignificantWhitespace, TokenKind.EndOfFile)) {
            }
            if (p2.Is(TokenKind.SignificantWhitespace) && GetIndentLength(p2) >= _currentIndentLength) {
                // Keep going if it's an unexpected indent - we'll add the
                // error when we read the whitespace.
                return PeekAhead(lookaheadCount + 1);
            }

            // Whitespace does not match expectation
            return Token.Empty;
        }

        private SourceSpan ReadCurrentStatementBreak(bool justDedented = false) {
            Debug.Assert(IsCurrentStatementBreak());

            var span = Next().Span;
            if (Peek.Is(TokenKind.SignificantWhitespace)) {
                var ws = Next();
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
            }
            return span;
        }

        private Statement ParseStmt() {
            var ws = ReadWhitespace();
            Statement stmt = null;

            try {
                if (Peek.Is(TokenUsage.EndStatement)) {
                    stmt = new EmptyStatement {
                        Span = new SourceSpan(Peek.Span.Start, 0)
                    };
                } else if (Peek.IsAny(TokenUsage.BeginStatement, TokenUsage.BeginStatementOrBinaryOperator)) {
                    stmt = ParseIdentifierAsStatement();
                } else if (Peek.Is(TokenKind.Comment)) {
                    stmt = new EmptyStatement {
                        Span = new SourceSpan(Peek.Span.Start, 0),
                        Comment = ReadComment(),
                        AfterNode = ReadWhitespace()
                    };
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
                stmt.BeforeNode = ws;

                if (!Peek.Is(TokenUsage.EndStatement)) {
                    ws = ReadWhitespace();
                    throw new ParseErrorException("invalid syntax", Peek.Span.Start);
                }
            } catch (ParseErrorException ex) {
                ReadUntil(IsEndOfStatement);
                stmt = new ErrorStatement(ex.Message) {
                    BeforeNode = ws,
                    Span = new SourceSpan(ws.End, Current.Span.End)
                };
                ReportError(ex.Message, new SourceSpan(ex.Location, Current.Span.End));
            }

            return stmt;
        }

        private Statement ParseIdentifierAsStatement() {
            var kind = Peek.Kind;
            bool isAsync = false;
            if (HasAsyncAwait && kind == TokenKind.KeywordAsync) {
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
                    return ParseDecorated();
                case TokenKind.KeywordDef:
                    return ParseFuncDef(isAsync);
                case TokenKind.KeywordClass:
                    return ParseClassDef();
                case TokenKind.KeywordWith:
                    if (HasWith) {
                        return ParseWithStmt(isAsync);
                    }
                    goto default;
                case TokenKind.KeywordPrint:
                    if (!HasPrintFunction) {
                        return ParsePrintStmt();
                    }
                    goto default;
                case TokenKind.KeywordPass:
                    return WithCommentAndWhitespace(new PassStatement {
                        Span = Read(TokenKind.KeywordPass),
                    });
                case TokenKind.KeywordBreak:
                    return WithCommentAndWhitespace(new BreakStatement {
                        Span = Read(TokenKind.KeywordBreak),
                    });
                case TokenKind.KeywordContinue:
                    return WithCommentAndWhitespace(new ContinueStatement {
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
                    if (HasNonlocal) {
                        return ParseNonlocalStmt();
                    }
                    goto default;
                case TokenKind.KeywordRaise:
                    return ParseRaiseStmt();
                case TokenKind.KeywordAssert:
                    return ParseAssertStmt();
                case TokenKind.KeywordExec:
                    if (HasExecStatement) {
                        return ParseExecStmt();
                    }
                    goto default;
                case TokenKind.KeywordDel:
                    return ParseDelStmt();
                case TokenKind.KeywordAsync:
                    throw new ParseErrorException("invalid syntax", Peek.Span.Start);
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
            stmt.Expression = ParseSingleExpression();

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
                    ReportError("'as' requires Python 2.6 or later", ae.AsSpan);
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

        private Statement ParseDecorated() {
            var start = Peek.Span.Start;
            Read(TokenKind.At);
            var decorator = ParsePrimaryWithTrailers();
            Statement inner = null;
            var next = GetTokenAfterCurrentStatementBreak();
            if (next.Is(TokenKind.At)) {
                ReadCurrentStatementBreak();
                inner = ParseDecorated();
            } else if (next.IsAny(TokenKind.KeywordDef, TokenKind.KeywordClass, TokenKind.KeywordAsync)) {
                ReadCurrentStatementBreak();
                inner = ParseStmt();
                if (inner is ClassDefinition && !HasClassDecorators) {
                    ReportError(
                        "invalid syntax, class decorators require 2.6 or later.",
                        new SourceSpan(start, decorator.Span.End)
                    );
                }
            } else {
                ReportError(
                    "invalid decorator, must be applied to function" + (HasClassDecorators ? " or class" : ""),
                    new SourceSpan(start, decorator.Span.End)
                );
            }
            return new DecoratorStatement {
                Expression = decorator,
                Inner = inner,
                Span = new SourceSpan(start, Current.Span.End)
            };
        }

        private Statement ParseFuncDef(bool isCoroutine) {
            var start = Peek.Span.Start;
            var stmt = new FunctionDefinition(TokenKind.KeywordDef);
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

                    if (!HasAnnotations) {
                        ReportError("invalid syntax, function annotations require 3.x", stmt.ReturnAnnotation.Span);
                    }

                    ws = ReadWhitespace();
                }
                stmt.BeforeColon = ws;
                Read(TokenKind.Colon);

                stmt.Body = ParseSuite();
            } finally {
                _scopes.Pop();
            }

            if (stmt.IsGenerator && !HasGeneratorReturn) {
                stmt.Walk(new ReturnInGeneratorErrorWalker { Parser = this });
            }

            stmt.Span = new SourceSpan(start, Current.Span.End);
            MaybeReadComment(stmt);
            return stmt;
        }

        private class ReturnInGeneratorErrorWalker : PythonWalker {
            public Parser Parser { get; set; }

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
                NameExpression = ReadMemberName()
            };

            if (TryRead(TokenKind.LeftParenthesis)) {
                stmt.Bases = ParseArgumentList(TokenKind.RightParenthesis, true);
                Read(TokenKind.RightParenthesis);
            }
            stmt.BeforeColon = ReadWhitespace();
            Read(TokenKind.Colon);
            _scopes.Push(stmt);
            try {
                stmt.Body = ParseSuite();
            } finally {
                _scopes.Pop();
            }

            stmt.Span = new SourceSpan(start, Current.Span.End);
            MaybeReadComment(stmt);
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
            return WithCommentAndWhitespace(stmt);
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
            var names = new List<NameExpression>();
            var asNames = new List<NameExpression>();
            var name = TryReadName(allowStar: true);
            while (name != null) {
                names.Add(name);
                if (TryRead(TokenKind.KeywordAs)) {
                    var asStart = Current.Span.Start;
                    asNames.Add(ReadName());
                    if (name.IsStar) {
                        ReportError(errorAt: new SourceSpan(asStart, Current.Span.End));
                    }
                } else {
                    asNames.Add(null);
                }

                if (name.IsStar) {
                    hasStar = true;
                    break;
                }

                if (!TryRead(TokenKind.Comma)) {
                    break;
                }

                name = TryReadName(allowStar: true);
                if (name == null) {
                    names.Add(null);
                    ReportError("trailing comma not allowed without surrounding parentheses", Current.Span);
                    break;
                }
            }

            stmt.Names = names;
            stmt.AsNames = asNames;

            if (stmt.HasParentheses) {
                Read(TokenKind.RightParenthesis);
            }

            stmt.Span = new SourceSpan(start, Current.Span.End);
            if (hasStar && _version.Is3x() && CurrentScope != null) {
                ReportError("import * only allowed at module level", stmt.Span);
            }

            if (stmt.Root.IsFuture) {
                foreach (var n in stmt.Names) {
                    if (string.IsNullOrEmpty(n?.Name)) {
                        continue;
                    }
                    switch (n.Name) {
                        case "division":
                            _future |= FutureOptions.TrueDivision;
                            break;
                        case "generators":
                            break;
                        case "with_statement":
                            _future |= FutureOptions.WithStatement;
                            break;
                        case "absolute_import":
                            _future |= FutureOptions.AbsoluteImports;
                            break;
                        case "print_function":
                            _future |= FutureOptions.PrintFunction;
                            if (_version < PythonLanguageVersion.V26) {
                                ReportError("future feature is not defined until 2.6: print_function", n.Span);
                            }
                            break;
                        case "unicode_literals":
                            _future |= FutureOptions.UnicodeLiterals;
                            if (_version < PythonLanguageVersion.V26) {
                                ReportError("future feature is not defined until 2.6: unicode_literals", n.Span);
                            }
                            break;
                        case "generator_stop":
                            _future |= FutureOptions.GeneratorStop;
                            if (_version < PythonLanguageVersion.V35) {
                                ReportError("future feature is not defined until 3.5: generator_stop", n.Span);
                            }
                            break;
                    }
                }
            }

            return WithCommentAndWhitespace(stmt);
        }

        private Statement ParseImportStmt() {
            var start = Read(TokenKind.KeywordImport).Start;
            var stmt = new ImportStatement();

            var names = new List<DottedName>();
            var asNames = new List<NameExpression>();
            var name = ReadDottedName();
            while (name != null) {
                names.Add(name);
                if (TryRead(TokenKind.KeywordAs)) {
                    var asStart = Current.Span.Start;
                    asNames.Add(ReadName());
                } else {
                    asNames.Add(null);
                }

                if (!TryRead(TokenKind.Comma)) {
                    break;
                }

                name = ReadDottedName();
                if (name == null) {
                    names.Add(null);
                    ReportError("trailing comma not allowed", Current.Span);
                    break;
                }
            }

            stmt.Names = names;
            stmt.AsNames = asNames;

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return WithCommentAndWhitespace(stmt);
        }

        private Statement ParseGlobalStmt() {
            var start = Read(TokenKind.KeywordGlobal).Start;
            var stmt = new GlobalStatement();

            do {
                stmt.AddName(ReadName());
            } while (TryRead(TokenKind.Comma));

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return WithCommentAndWhitespace(stmt);
        }

        private Statement ParseNonlocalStmt() {
            var start = Read(TokenKind.KeywordNonlocal).Start;

            if (CurrentScope == null) {
                ReportError("nonlocal declaration not allowed at module level", Current.Span);
            }

            var stmt = new NonlocalStatement();

            do {
                stmt.AddName(ReadName());
            } while (TryRead(TokenKind.Comma));

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return WithCommentAndWhitespace(stmt);
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

            return WithComment(stmt);
        }

        private Statement ParseAssertStmt() {
            var start = Read(TokenKind.KeywordAssert).Start;
            var stmt = new AssertStatement {
                Expression = ParseExpression()
            };

            stmt.Span = new SourceSpan(start, Current.Span.End);

            return WithComment(stmt);
        }

        private Statement ParseExecStmt() {
            var start = Read(TokenKind.KeywordExec).Start;

            var stmt = new ExecStatement {
                Expression = ParseExpression()
            };
            stmt.Span = new SourceSpan(start, Current.Span.End);

            stmt.CheckSyntax(this);

            return WithCommentAndWhitespace(stmt);
        }

        private Statement ParseDelStmt() {
            var start = Read(TokenKind.KeywordDel).Start;

            var stmt = new DelStatement();

            do {
                var expr = ParseSingleExpression();
                expr.CheckDelete(this);
                stmt.AddExpression(expr);
            } while (TryRead(TokenKind.Comma));

            stmt.Span = new SourceSpan(start, Current.Span.End);
            return WithComment(stmt);
        }

        private Statement ParseExprStmt() {
            var expr = ParseExpression();

            if (!Peek.Is(TokenUsage.Assignment)) {
                return WithCommentAndWhitespace(new ExpressionStatement {
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

                return WithCommentAndWhitespace(new AssignmentStatement {
                    Targets = targets,
                    Expression = expr,
                    Span = new SourceSpan(targets[0].Span.Start, Current.Span.End)
                });
            }

            expr.CheckAugmentedAssign(this);
            return WithComment(new AugmentedAssignStatement {
                Target = expr,
                Operator = Next().Kind.GetBinaryOperator(),
                Expression = ParseExpression()
            });

        }


        private Expression ParseAssignmentTarget(bool alwaysTuple = false) {
            var start = Peek.Span.Start;
            var targets = new List<Expression>();

            while (true) {
                var ws = ReadWhitespace();
                var expr = HasStarUnpacking ? ParseStarName() : ParsePrimaryWithTrailers();
                Debug.Assert(expr.BeforeNode.Length == 0, "Should not have read leading whitespace");
                expr.BeforeNode = ws;
                expr.Freeze();

                targets.Add(expr);

                if (!TryRead(TokenKind.Comma)) {
                    if (targets.Count == 1 && !alwaysTuple) {
                        return expr;
                    }
                    break;
                }
            }
            var tuple = WithCommentAndWhitespace(new TupleExpression {
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
            tuple.AddItem(expr);

            while (TryRead(TokenKind.Comma)) {
                expr = ParseSingleExpression(
                    allowIfExpr: allowIfExpr,
                    allowSlice: allowSlice,
                    allowGenerator: allowGenerator
                );
                tuple.AddItem(expr);
            }

            tuple.Span = new SourceSpan(start, expr.Span.End);
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
                ReportError(ex.Message, new SourceSpan(ex.Location, Current.Span.End));
                return new ErrorExpression {
                    Span = new SourceSpan(ex.Location, Current.Span.End)
                };
            }

            MaybeReadComment(expr);

            if (allowIfExpr && Peek.Is(TokenKind.KeywordIf)) {
                expr.Freeze();
                var condExpr = new ConditionalExpression { TrueExpression = expr };
                Read(TokenKind.KeywordIf);
                condExpr.Expression = ParseSingleExpression();
                Read(TokenKind.KeywordElse);
                condExpr.FalseExpression = ParseSingleExpression();
                condExpr.Span = new SourceSpan(expr.Span.Start, Current.Span.End);
                expr = condExpr;
            }

            MaybeReadComment(expr);

            if (allowSlice && Peek.Is(TokenKind.Colon)) {
                expr.Freeze();
                var sliceExpr = new SliceExpression { SliceStart = expr };
                Read(TokenKind.Colon);
                sliceExpr.SliceStop = ParseSingleExpression(allowSlice: false, allowGenerator: allowGenerator);

                if (TryRead(TokenKind.Colon)) {
                    sliceExpr.SliceStep = ParseSingleExpression(allowSlice: false, allowGenerator: allowGenerator);
                }

                sliceExpr.Span = new SourceSpan(expr.Span.Start, Current.Span.End);
                expr = sliceExpr;
            }

            MaybeReadComment(expr);

            if (allowGenerator && Peek.Is(TokenKind.KeywordFor)) {
                expr.Freeze();
                var genExpr = new GeneratorExpression {
                    Item = expr,
                    Iterators = ReadComprehension()
                };

                genExpr.Span = new SourceSpan(expr.Span.Start, Current.Span.End);
                expr = genExpr;
            }

            expr.BeforeNode = ws;
            MaybeReadComment(expr);
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
            tuple.AddItem(expr);

            while (TryRead(TokenKind.Comma)) {
                expr = ParseSingleAsExpression();
                tuple.AddItem(expr);
            }

            tuple.Span = new SourceSpan(start, expr.Span.End);
            tuple.Freeze();
            return tuple;
        }

        private Expression ParseSingleAsExpression() {
            var expr = ParseSingleExpression();
            if (PeekNonWhitespace.Is(TokenKind.KeywordAs)) {
                var start = expr.Span.Start;
                var asExpr = new AsExpression {
                    Expression = expr,
                    BeforeAs = ReadWhitespace(),
                    Span = Read(TokenKind.KeywordAs),
                    BeforeName = ReadWhitespace(),
                    Name = ReadName(),
                    Comment = ReadComment(),
                    AfterNode = ReadWhitespace()
                };
                asExpr.Span = new SourceSpan(
                    asExpr.Expression.Span.Start,
                    asExpr.Name.Span.End
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


            expr.Expression = ParseSingleExpression();
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
                    if (HasSublistParameters) {
                        p.Sublist = sublist;
                    } else {
                        ReportError("sublist parameters are not supported in 3.x", sublist.Span);
                    }
                    Read(TokenKind.RightParenthesis);
                }

                if (p.Kind != ParameterKind.Sublist) {
                    if (HasBareStarParameter && p.Kind == ParameterKind.List && !Peek.Is(TokenKind.Name)) {
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
                        if (!HasAnnotations) {
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
                WithCommentAndWhitespace(p);
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

            parameters.Comment = ReadComment();
            parameters.Freeze();
            return parameters;
        }

        private List<Arg> ParseArgumentList(TokenKind closing, bool allowNames, bool allowSlice = false) {
            var args = new List<Arg>();
            var names = new HashSet<string>();

            while (!Peek.Is(closing)) {
                var a = new Arg();

                var expr = ParseSingleExpression(allowSlice: allowSlice);

                if (allowNames && TryRead(TokenKind.Assign)) {
                    var name = (expr as NameExpression)?.Name;
                    if (string.IsNullOrEmpty(name)) {
                        ReportError("expected name", expr.Span);
                    } else if (!names.Add(name)) {
                        ReportError("keyword argument repeated", expr.Span);
                    }
                    a.NameExpression = expr;
                    a.Expression = ParseSingleExpression(allowSlice: allowSlice);
                } else {
                    a.Expression = expr;
                }

                MaybeReadComment(a);
                a.HasCommaAfterNode = TryRead(TokenKind.Comma);
                a.Freeze();

                args.Add(a);
            }

            return args;
        }

        private Expression ParseComparison() {
            var expr = ParseNotTest();
            var withinOp = SourceSpan.None;

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

            return WithCommentAndWhitespace(new BinaryExpression {
                Left = expr,
                Operator = op,
                WithinOperator = withinOp,
                Right = rhs,
                Span = new SourceSpan(expr.Span.Start, rhs.Span.End)
            });
        }

        private Expression ParseNotTest() {
            if (TryRead(TokenKind.KeywordNot)) {
                var start = Current.Span.Start;
                var ws = ReadWhitespace();
                var expr = ParseStarExpression();
                expr.BeforeNode = ws;
                expr.Freeze();
                return new UnaryExpression {
                    Operator = PythonOperator.Not,
                    Expression = expr,
                    Span = new SourceSpan(start, expr.Span.End)
                };
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
                return new StarredExpression(kind, expr) {
                    Span = new SourceSpan(start, expr.Span.End)
                };
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
                return new StarredExpression(kind, expr) {
                    Span = new SourceSpan(start, expr.Span.End)
                };
            }

            return ParseExpr();
        }

        private Expression ParseExpr(int precedence = 0) {
            var expr = ParseFactor();
            while (Peek.IsAny(TokenUsage.BinaryOperator, TokenUsage.BinaryOrUnaryOperator)) {
                var op = Peek.Kind.GetBinaryOperator();
                if (op == PythonOperator.None) {
                    return expr;
                } else if (op == PythonOperator.MatMultiply && _version < PythonLanguageVersion.V35) {
                    ReportError(errorAt: Next().Span);
                    return expr;
                }

                var prec = op.GetPrecedence();
                if (prec >= precedence) {
                    Next();

                    var ws = ReadWhitespace();
                    var rhs = ParseExpr(prec);
                    rhs.BeforeNode = ws;
                    rhs.Freeze();

                    expr = WithCommentAndWhitespace(new BinaryExpression {
                        Left = expr,
                        Operator = op,
                        Right = rhs,
                        Span = new SourceSpan(expr.Span.Start, rhs.Span.End)
                    });
                }
            }
            return expr;
        }

        private Expression ParseFactor() {
            Expression topExpr = null;
            UnaryExpression unaryExpr = null;
            AwaitExpression awaitExpr = null;
            ExpressionWithExpression yieldExpr = null;

            SourceSpan ws = ReadWhitespace();
            var start = Peek.Span.Start;
            while (Peek.IsAny(TokenUsage.UnaryOperator, TokenUsage.BinaryOrUnaryOperator)) {
                var expr = new UnaryExpression() {
                    BeforeNode = ws
                };
                switch (Next().Kind) {
                    case TokenKind.Add:
                        expr.Operator = PythonOperator.Pos;
                        break;
                    case TokenKind.Subtract:
                        expr.Operator = PythonOperator.Negate;
                        break;
                    case TokenKind.Twiddle:
                        expr.Operator = PythonOperator.Invert;
                        break;
                    default:
                        ThrowError(errorAt: Current.Span.Start);
                        return null;
                }
                MaybeReadComment(expr);
                if (unaryExpr == null) {
                    topExpr = unaryExpr = expr;
                    expr.Span = new SourceSpan(start, Current.Span.End);
                } else {
                    unaryExpr.Expression = expr;
                    unaryExpr.Span = new SourceSpan(unaryExpr.Span.Start, expr.Span.End);
                    unaryExpr = expr;
                }
                ws = ReadWhitespace();
            }

            if (HasAsyncAwait && IsInAsyncFunction && TryRead(TokenKind.KeywordAwait)) {
                start = Current.Span.Start;
                var beforeExpr = ReadWhitespace();
                var expr = ParsePower();
                expr.BeforeNode = beforeExpr;
                expr.Freeze();
                awaitExpr = new AwaitExpression {
                    BeforeNode = ws,
                    Expression = expr
                };
                if (unaryExpr != null) {
                    unaryExpr.Expression = awaitExpr;
                } else {
                    topExpr = awaitExpr;
                }
                awaitExpr.Span = new SourceSpan(start, Current.Span.End);
                ws = ReadWhitespace();
            }

            if (TryRead(TokenKind.KeywordYield)) {
                var yieldSpan = Current.Span;
                bool shownError = false;
                var func = CurrentScope as FunctionDefinition;
                if (func != null) {
                    func.IsGenerator = true;
                } else if (PeekNonWhitespace.Is(TokenKind.KeywordFrom)) {
                    ReportError(
                        HasYieldFrom ? "'yield from' outside of generator" : "'yield from' requires Python 3.3 or later",
                        new SourceSpan(yieldSpan.Start, PeekNonWhitespace.Span.End)
                    );
                    shownError = true;
                } else {
                    ReportError("'yield' outside of generator", yieldSpan);
                    shownError = true;
                }

                Expression expr;

                if (PeekNonWhitespace.Is(TokenKind.KeywordFrom)) {
                    if (!shownError && IsInAsyncFunction) {
                        ReportError("'yield from' in async function", yieldSpan);
                    }

                    yieldExpr = new YieldFromExpression {
                        BeforeNode = ws,
                        BeforeFrom = ReadWhitespace()
                    };
                    Read(TokenKind.KeywordFrom);
                    expr = ParseSingleExpression(allowGenerator: false);
                    if (Peek.Is(TokenKind.Comma)) {
                        // Cannot yield from a tuple
                        ThrowError();
                    }

                    yieldExpr.Expression = expr;
                    if (!shownError && expr is EmptyExpression) {
                        ReportError(errorAt: new SourceSpan(yieldSpan.Start, Current.Span.End));
                    }
                } else {
                    if (!shownError && IsInAsyncFunction) {
                        ReportError("'yield' in async function", yieldSpan);
                    }

                    yieldExpr = new YieldExpression {
                        BeforeNode = ws,
                        Expression = ParseExpression(allowGenerator: false)
                    };
                }

                if (unaryExpr != null) {
                    unaryExpr.Expression = yieldExpr;
                } else {
                    topExpr = yieldExpr;
                }
                yieldExpr.Span = new SourceSpan(yieldSpan.Start, Current.Span.End);
            }

            if (unaryExpr != null && unaryExpr.Expression == null) {
                unaryExpr.Expression = ParsePower();
            }

            return topExpr ?? ParsePower();
        }

        private Expression ParsePower() {
            var expr = ParsePrimaryWithTrailers();
            if (TryRead(TokenKind.Power)) {
                var ws = ReadWhitespace();
                var rhs = ParseFactor();
                rhs.BeforeNode = ws;
                rhs.Freeze();

                expr = WithCommentAndWhitespace(new BinaryExpression {
                    Left = expr,
                    Operator = PythonOperator.Power,
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
                        expr = WithComment(expr);
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
                        return expr;
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
                    return new ParenthesisExpression {
                        Expression = ParseExpression(),
                        Span = new SourceSpan(start, Read(TokenKind.RightParenthesis).End),
                        Comment = ReadComment(),
                        AfterNode = ReadWhitespace()
                    };

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
                } else if (!HasWith && Peek.Is(TokenKind.KeywordWith)) {
                    // pass
                } else if (!HasAs && Peek.Is(TokenKind.KeywordAs)) {
                    // pass
                } else if (HasPrintFunction && Peek.Is(TokenKind.KeywordPrint)) {
                    // pass
                } else if (!HasNonlocal && Peek.Is(TokenKind.KeywordNonlocal)) {
                    // pass
                } else if (!HasExecStatement && Peek.Is(TokenKind.KeywordExec)) {
                    // pass
                } else if (Peek.IsAny(TokenUsage.EndGroup, TokenUsage.EndStatement) ||
                    Peek.Is(TokenCategory.Delimiter)) {
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
                    return new NameExpression(text) {
                        Span = Current.Span,
                        Comment = ReadComment(),
                        AfterNode = ReadWhitespace()
                    };

                case TokenKind.KeywordPrint:
                    var printToken = Next();
                    if (PeekNonWhitespace.Is(TokenUsage.EndStatement) ||
                        PeekNonWhitespace.IsAny(TokenKind.LeftParenthesis, TokenKind.Comment)) {
                        return new NameExpression("print") {
                            Span = printToken.Span,
                            Comment = ReadComment(),
                            AfterNode = ReadWhitespace()
                        };
                    } else {
                        var ws = ReadWhitespace();
                        var start = Peek.Span.Start;
                        ReadUntil(t => t.IsAny(TokenUsage.EndGroup, TokenUsage.EndStatement));
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
                    return WithCommentAndWhitespace(new ConstantExpression {
                        Span = Next().Span,
                        Value = null
                    });

                case TokenKind.KeywordTrue:
                    if (HasConstantBooleans) {
                        return WithCommentAndWhitespace(new ConstantExpression {
                            Span = Next().Span,
                            Value = true
                        });
                    } else {
                        return WithCommentAndWhitespace(new NameExpression("True") { Span = Next().Span });
                    }
                case TokenKind.KeywordFalse:
                    if (HasConstantBooleans) {
                        return WithCommentAndWhitespace(new ConstantExpression {
                            Span = Next().Span,
                            Value = false
                        });
                    } else {
                        return WithCommentAndWhitespace(new NameExpression("False") { Span = Next().Span });
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
                        return WithCommentAndWhitespace(new ErrorExpression {
                            Span = Current.Span
                        }); 
                    }
                    return WithCommentAndWhitespace(new ConstantExpression {
                        Span = Current.Span,
                        Value = value
                    });

                case TokenKind.Ellipsis:
                    if (_version.Is2x()) {
                        ReportError("unexpected token '.'");
                    }
                    return WithCommentAndWhitespace(new ConstantExpression {
                        Span = Next().Span,
                        Value = Ellipsis.Value
                    });

                default:
                    return WithCommentAndWhitespace(new ErrorExpression {
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
                    if (!double.TryParse(text, out d)) {
                        return false;
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
                    if (!HasUnicodePrefix) {
                        ReportError("invalid syntax", opening.Span);
                    } else if (isBytes) {
                        ReportError("b and u prefixes are not compatible", opening.Span);
                    } else if (_version.Is3x() && isRaw) {
                        ReportError("r and u prefixes are not compatible", opening.Span);
                    }
                }
                if (isBytes) {
                    if (!HasBytesPrefix) {
                        ReportError("invalid syntax", opening.Span);
                    } else if (isRaw && !HasRawBytesPrefix) {
                        ReportError("invalid syntax", opening.Span);
                    }
                }
                if (!isBytes && !isUnicode) {
                    isUnicode = HasUnicodeLiterals;
                    isBytes = !isUnicode;
                }

                var closing = opening.Kind.GetGroupEnding();
                var quoteStart = Current.Span.Start;

                var fullText = new StringBuilder();
                while (TryRead(TokenKind.LiteralString)) {
                    var text = _tokenization.GetTokenText(Current);
                    // Exclude newline if escaped
                    int lastEscape = text.LastIndexOf('\\');
                    if (lastEscape >= 0 && lastEscape + 1 < text.Length) {
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

                var expr = WithCommentAndWhitespace(new ConstantExpression {
                    Value = isBytes ?
                        new AsciiString(_tokenization.Encoding.GetBytes(fullText.ToString()), fullText.ToString()) :
                        (object)fullText.ToString(),
                    Span = new SourceSpan(quoteStart, Current.Span.End)
                });
                expr.Freeze();

                parts.Add(expr);
            }

            return WithCommentAndWhitespace(new StringExpression {
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

                char c = str[++i];
                switch (c) {
                    case '\\': res.Append('\\'); break;
                    case '\'': res.Append('\''); break;
                    case '"': res.Append('"'); break;
                    case 'a': res.Append('\a'); break;
                    case 'b': res.Append('\b'); break;
                    case 'f': res.Append('\f'); break;
                    case 'n': res.Append('\n'); break;
                    case 'r': res.Append('\r'); break;
                    case 't': res.Append('\t'); break;
                    case 'v': res.Append('\v'); break;
                    case 'x': res.Append(ReadEscape(str, strStart, i - 1, "\\x00", isUnicode)); i += 2; break;
                    case 'u': res.Append(ReadEscape(str, strStart, i - 1, "\\uxxxx", isUnicode)); i += 4; break;
                    case 'U': res.Append(ReadEscape(str, strStart, i - 1, "\\Uxxxxxxxx", isUnicode)); i += 8; break;
                    default:
                        if (CharUnicodeInfo.GetDigitValue(c) >= 0 && CharUnicodeInfo.GetDigitValue(c) <= 7) {
                            res.Append(ReadEscape(str, strStart, i - 1, "\\ooo", isUnicode));
                            i += 2;
                        } else {
                            res.Append("\\");
                            res.Append(c);
                        }
                        break;
                }

                prev = i + 1;
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

            if (!HasReprLiterals) {
                ReportError("invalid syntax", expr.Span);
            }

            return expr;
        }

        private Expression ParseListLiteralOrComprehension() {
            var start = Read(TokenKind.LeftBracket).Start;

            if (TryRead(TokenKind.RightBracket)) {
                return WithCommentAndWhitespace(new ListExpression {
                    Span = new SourceSpan(start, Current.Span.End)
                });
            }

            var expr = ParseSingleExpression(allowSlice: false, allowGenerator: false);
            if (PeekNonWhitespace.Is(TokenKind.KeywordFor)) {
                return WithCommentAndWhitespace(new ListComprehension {
                    Item = expr,
                    Iterators = ReadComprehension(),
                    Span = new SourceSpan(start, Read(TokenKind.RightBracket).End)
                });
            }

            var list = new ListExpression();
            while (true) {
                list.AddItem(expr);

                if (TryRead(TokenKind.RightBracket)) {
                    list.Span = new SourceSpan(start, Current.Span.End);
                    return WithCommentAndWhitespace(list);
                }

                Read(TokenKind.Comma);

                expr = ParseSingleExpression(allowGenerator: false, allowSlice: true);
            }
        }

        private Expression ParseDictOrSetLiteralOrComprehension() {
            var start = Read(TokenKind.LeftBrace).Start;

            if (TryRead(TokenKind.RightBrace)) {
                return WithCommentAndWhitespace(new DictionaryExpression {
                    Span = new SourceSpan(start, Current.Span.End)
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
            return WithCommentAndWhitespace(expr);
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
                if (!HasDictComprehensions) {
                    ReportError(
                        "invalid syntax, dictionary comprehensions require Python 2.7 or later",
                        errorAt: new SourceSpan(sliceExpr.Span.Start, Current.Span.End)
                    );
                }
                return comp;
            }

            var dict = new DictionaryExpression();

            while (true) {
                dict.AddItem(sliceExpr);

                if (Peek.Is(TokenKind.RightBrace)) {
                    return dict;
                }

                Read(TokenKind.Comma);

                expr = ParseSingleExpression(allowGenerator: false, allowSlice: true);
                sliceExpr = expr as SliceExpression;
                if (sliceExpr == null && !(expr is EmptyExpression)) {
                    ReportError(errorAt: expr.Span);
                }
            }
        }

        private Expression ParseSetLiteralOrComprehension(Expression expr) {
            if (PeekNonWhitespace.Is(TokenKind.KeywordFor)) {
                var comp = new SetComprehension {
                    Item = expr,
                    Iterators = ReadComprehension()
                };
                if (!HasSetLiterals) {
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
                set.AddItem(expr);

                if (Peek.Is(TokenKind.RightBrace) || Peek.Is(TokenUsage.EndStatement)) {
                    if (!HasSetLiterals) {
                        ReportError(
                            "invalid syntax, set literals require Python 2.7 or later",
                            new SourceSpan(start, Current.Span.End)
                        );
                    }
                    return set;
                }

                Read(TokenKind.Comma);

                expr = ParseSingleExpression(allowGenerator: false, allowSlice: true);
            }
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
                    if (HasTupleAsComprehensionTarget) {
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

                MaybeReadComment(it);
                it.Span = new SourceSpan(start, Current.Span.End);
                iterators.Add(it);
            }
            return iterators;
        }

        private void ReadCompoundStatement(CompoundStatement stmt) {
            var start = stmt.Span.IsValid ? stmt.Span.Start : Current.Span.Start;

            stmt.BeforeColon = ReadWhitespace();
            Read(TokenKind.Colon);
            stmt.Comment = ReadComment();
            stmt.AfterComment = ReadWhitespace();
            stmt.Body = ParseSuite();
            stmt.Span = new SourceSpan(start, Current.Span.End);
        }

        #endregion

        #region Read functions

        private SourceSpan ReadWhitespace() {
            SourceLocation start = Peek.Span.Start, end = Peek.Span.Start;

            while (Peek.IsAny(TokenKind.Whitespace, TokenKind.ExplicitLineJoin) ||
                Current.Is(TokenKind.ExplicitLineJoin) && Peek.Is(TokenKind.NewLine)
            ) {
                end = Next().Span.End;
            }

            return new SourceSpan(start, end);
        }

        private void MaybeReadComment(Node node) {
            if (Peek.Is(TokenKind.Comment)) {
                Debug.Assert(node.Comment == null, "Already read one comment");
                node.Comment = ReadComment();
            }
            MaybeReadWhitespaceAfterNode(node);
        }


        private void MaybeReadWhitespaceAfterNode(Node node) {
            if (Peek.Is(TokenKind.Whitespace)) {
                Debug.Assert(node.AfterNode.Length == 0, "Already read whitespace");
                node.AfterNode = ReadWhitespace();
            }
        }

        private CommentExpression ReadComment() {
            if (PeekNonWhitespace.Is(TokenCategory.Comment)) {
                return new CommentExpression {
                    BeforeNode = ReadWhitespace(),
                    Span = Next().Span,
                    AfterNode = ReadWhitespace()
                };
            }
            return null;
        }

        private SourceSpan ReadNewLine() {
            if (Peek.Is(TokenKind.NewLine)) {
                return Next().Span;
            }
            return SourceSpan.None;
        }

        private T WithComment<T>(T target) where T : Node {
            target.Comment = ReadComment();
            return target;
        }

        private T WithCommentAndWhitespace<T>(T target) where T : Node {
            MaybeReadComment(target);
            return target;
        }

        private NameExpression ReadMemberName(string error = "invalid syntax", SourceLocation? errorAt = null) {
            var prefix = CurrentClass?.Name;
            if (!string.IsNullOrEmpty(prefix)) {
                prefix = "_" + prefix.TrimStart('_');
            }
            return ReadName(prefix, error: error, errorAt: errorAt);
        }

        private DottedName ReadDottedName() {
            var names = new List<NameExpression>();
            var expr = new DottedName {
                BeforeNode = ReadWhitespace(),
                Names = names
            };
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
                    ne = WithCommentAndWhitespace(NameExpression.Empty(start + 2));
                    ne.Freeze();
                    names.Add(ne);
                    continue;
                }

                if (p.Is(TokenKind.Dot)) {
                    var ws = ReadWhitespace();
                    var start = Next().Span.Start;
                    var ne = WithCommentAndWhitespace(NameExpression.Empty(start, ws));
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
            return WithCommentAndWhitespace(expr);
        }

        private NameExpression TryReadName(string prefix = null, bool allowStar = false) {
            var before = ReadWhitespace();

            string name;
            switch (Peek.Kind) {
                case TokenKind.Name:
                    name = _tokenization.GetTokenText(Peek);
                    break;
                case TokenKind.KeywordAsync:
                case TokenKind.KeywordAwait:
                    if (HasAsyncAwait && IsInAsyncFunction) {
                        return null;
                    }
                    name = Peek.Is(TokenKind.KeywordAsync) ? "async" : "await";
                    break;
                case TokenKind.KeywordWith:
                    if (HasWith) {
                        return null;
                    }
                    name = "with";
                    break;
                case TokenKind.KeywordAs:
                    if (HasAs) {
                        return null;
                    }
                    name = "as";
                    break;
                case TokenKind.KeywordNonlocal:
                    if (HasNonlocal) {
                        return null;
                    }
                    name = "nonlocal";
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

            var expr = new NameExpression(name, prefix) {
                BeforeNode = before,
                Span = Next().Span,
            };

            WithCommentAndWhitespace(expr);
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
                    } else if (_lookahead[i].Is(TokenKind.ExplicitLineJoin)) {
                        FillLookahead(i + 3);
                        if (_lookahead[i + 1].IsAny(TokenKind.NewLine, TokenKind.EndOfFile)) {
                            i += 2;
                        } else {
                            return _lookahead[i];
                        }
                    } else if (_lookahead[i].Is(TokenKind.Whitespace)) {
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
