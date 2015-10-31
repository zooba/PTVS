using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
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
        private SourceSpan _currentIndent;
        private bool _singleLine;
        private ErrorSink _errors;
        private Token _eofToken;

        public Parser(Tokenization tokenization) {
            _tokenization = tokenization;
            _version = _tokenization.LanguageVersion;
        }

        private void Reset() {
            _tokenEnumerator = _tokenization.AllTokens.GetEnumerator();
            _lookahead = new List<Token>();
            IsInAsyncFunction = false;
        }

        #region Language Features

        private bool HasAnnotations => _version >= PythonLanguageVersion.V30;

        private bool HasAsyncAwait => _version >= PythonLanguageVersion.V35;

        private bool HasWith => _version >= PythonLanguageVersion.V26 || _future.HasFlag(FutureOptions.WithStatement);

        private bool HasPrintFunction => _version.Is3x() || _future.HasFlag(FutureOptions.PrintFunction);

        private bool HasTrueDivision => _version.Is3x() || _future.HasFlag(FutureOptions.TrueDivision);

        private bool IsInAsyncFunction { get; set; }

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
            var prevSingleLine = _singleLine;

            _singleLine = !assumeMultiLine && !TryRead(TokenKind.NewLine);
            var indent = TryRead(TokenKind.SignificantWhitespace) ? Current.Span : SourceSpan.None;
            _currentIndent = indent;

            // Leading whitespace is already read; it will be attached to the
            // suite rather than each statement.
            var stmt = ParseStmt();

            while (IsCurrentStatementBreak()) {
                stmt.AfterNode = ReadCurrentStatementBreak();
                stmt.Freeze();

                body.Add(stmt);

                stmt = ParseStmt();
            }

            stmt.Freeze();
            body.Add(stmt);

            _currentIndent = prevIndent;
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
            p2 = PeekAhead(2);
            if (p2.Is(TokenKind.SignificantWhitespace)) {
                if (p2.Span.Length >= _currentIndent.Length) {
                    // Keep going if it's an unexpected indent - we'll add the
                    // error when we read the whitespace.
                    return PeekAhead(3);
                }
            } else if (_currentIndent.Length == 0) {
                return PeekAhead(2);
            }

            // Whitespace does not match expectation
            return Token.Empty;
        }

        private SourceSpan ReadCurrentStatementBreak() {
            Debug.Assert(IsCurrentStatementBreak());

            var span = Next().Span;
            if (Peek.Is(TokenKind.SignificantWhitespace)) {
                var ws = Next();
                if (ws.Span.Length > _currentIndent.Length) {
                    _errors.Add("unexpected indent", ws.Span, 0, Severity.Error);
                }
            }
            return span;
        }

        private Statement ParseStmt() {
            var ws = ReadWhitespace();
            Statement stmt = null;

            try {
                if (Peek.Is(TokenUsage.EndStatement)) {
                    stmt = new EmptyStatement();
                } else if (Peek.IsAny(TokenUsage.BeginStatement, TokenUsage.BeginStatementOrBinaryOperator)) {
                    stmt = ParseIdentifierAsStatement();
                } else if (Peek.Is(TokenKind.Comment)) {
                    stmt = new EmptyStatement {
                        Comment = ReadComment(),
                        AfterNode = ReadWhitespace()
                    };
                } else {
                    stmt = ParseExprStmt();
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
                kind = PeekAhead(2).Kind;
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
                    return WithComment(new PassStatement {
                        Span = Read(TokenKind.KeywordPass),
                    });
                case TokenKind.KeywordBreak:
                    return WithComment(new BreakStatement {
                        Span = Read(TokenKind.KeywordBreak),
                    });
                case TokenKind.KeywordContinue:
                    return WithComment(new ContinueStatement {
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
                    test.Test = ParseSingleExpression();
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
                Test = ParseSingleExpression()
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
                stmt.Span = Read(TokenKind.KeywordAsync);
                stmt.AfterAsync = ReadWhitespace();
                Read(TokenKind.KeywordFor);
            } else {
                stmt.Span = Read(TokenKind.KeywordFor);
            }

            stmt.Index = ParseAssignmentTarget();
            Read(TokenKind.KeywordIn);
            stmt.List = ParseSingleExpression();

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
                    handler.Test = ParseAsExpressions();
                }
                ReadCompoundStatement(handler);

                AsExpression ae;
                if (_version < PythonLanguageVersion.V26 && (ae = handler.Test as AsExpression) != null) {
                    ReportError("'as' requires Python 2.6 or later", ae.AsSpan);
                }

                TupleExpression te;
                if ((te = handler.Test as TupleExpression) != null) {
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
            return null;
        }

        private Statement ParseFuncDef(bool isCoroutine) {
            return null;
        }

        private Statement ParseClassDef() {
            return null;
        }

        private Statement ParseWithStmt(bool isAsync) {
            var stmt = new WithStatement();
            if (isAsync) {
                stmt.Span = Read(TokenKind.KeywordAsync);
                stmt.AfterAsync = ReadWhitespace();
                Read(TokenKind.KeywordWith);
            } else {
                stmt.Span = Read(TokenKind.KeywordWith);
            }

            stmt.Test = ParseAsExpressions();

            ReadCompoundStatement(stmt);
            return stmt;
        }

        private Statement ParsePrintStmt() {
            var stmt = new PrintStatement {
                Span = Read(TokenKind.KeywordPrint)
            };

            if (PeekNonWhitespace.Is(TokenKind.RightShift)) {
                stmt.BeforeLeftShift = ReadWhitespace();
                Read(TokenKind.RightShift);
                stmt.BeforeDestination = ReadWhitespace();
                stmt.Destination = ParseSingleExpression();
                if (!TryRead(TokenKind.Comma)) {
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

            return stmt;
        }

        private Statement ParseReturnStmt() {
            return null;
        }

        private Statement ParseFromImportStmt() {
            return null;
        }

        private Statement ParseImportStmt() {
            return null;
        }

        private Statement ParseGlobalStmt() {
            return null;
        }

        private Statement ParseNonlocalStmt() {
            return null;
        }

        private Statement ParseRaiseStmt() {
            return null;
        }

        private Statement ParseAssertStmt() {
            return null;
        }

        private Statement ParseExecStmt() {
            return null;
        }

        private Statement ParseDelStmt() {
            return null;
        }

        private Statement ParseYieldStmt() {
            return null;
        }

        private Statement ParseExprStmt() {
            return new ExpressionStatement(ParseSingleExpression());
        }


        private Expression ParseAssignmentTarget() {
            var targets = new List<Expression>();

            while (true) {
                var ws = ReadWhitespace();
                var expr = ParseStarName();
                Debug.Assert(expr.BeforeNode.Length == 0, "Should not have read leading whitespace");
                expr.BeforeNode = ws;
                expr.Freeze();

                targets.Add(expr);

                if (!TryRead(TokenKind.Comma)) {
                    if (targets.Count == 1) {
                        return expr;
                    }
                    break;
                }
            }
            var tuple = new TupleExpression {
                Items = targets,
            };
            tuple.Freeze();
            return tuple;
        }

        private Expression ParseExpression() {
            var expr = ParseSingleExpression();
            if (!Peek.Is(TokenKind.Comma)) {
                return expr;
            }

            var start = expr.Span.Start;
            var tuple = new TupleExpression();
            tuple.AddItem(expr);

            while (TryRead(TokenKind.Comma)) {
                expr = ParseSingleExpression();
                tuple.AddItem(expr);
            }

            tuple.Span = new SourceSpan(start, expr.Span.End);
            tuple.Freeze();
            return tuple;
        }

        private Expression ParseSingleExpression() {
            if (PeekNonWhitespace.Is(TokenKind.KeywordLambda)) {
                return ParseLambda();
            }

            var ws = ReadWhitespace();
            var expr = ParseComparison();

            expr.BeforeNode = ws;
            expr.Comment = ReadComment();
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

            while (true) {
                if (forLambda && Peek.Is(TokenKind.Colon) ||
                    !forLambda && Peek.Is(TokenKind.RightParenthesis)) {
                    break;
                }

                var p = new Parameter();

                if (TryRead(TokenKind.Multiply)) {
                    p.Kind = ParameterKind.List;
                } else if (TryRead(TokenKind.Power)) {
                    p.Kind = ParameterKind.Dictionary;
                }

                if (Peek.Is(TokenKind.Name)) {
                    p.NameExpression = ReadName();
                }

                if (!forLambda && HasAnnotations && TryRead(TokenKind.Colon)) {
                    p.Annotation = ParseSingleExpression();
                }

                if (TryRead(TokenKind.Assign)) {
                    p.DefaultValue = ParseSingleExpression();
                }

                if (TryRead(TokenKind.Comma)) {
                    p.HasComma = true;
                }

                p.Comment = ReadComment();
                p.Comment?.Freeze();
                p.AfterNode = ReadWhitespace();
                p.Freeze();

                parameters.AddParameter(p);
            }

            parameters.Comment = ReadComment();
            parameters.Freeze();
            return parameters;
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

            return new BinaryExpression {
                Left = expr,
                Operator = op,
                WithinOperator = withinOp,
                Right = rhs,
                Comment = ReadComment(),
                AfterNode = ReadWhitespace()
            };
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
            if (TryRead(TokenKind.Multiply)) {
                var start = Current.Span.Start;
                var ws = ReadWhitespace();
                var expr = ParsePrimaryWithTrailers();
                expr.BeforeNode = ws;
                expr.Freeze();
                return new StarredExpression(expr) {
                    Span = new SourceSpan(start, expr.Span.End)
                };
            }

            return ParsePrimaryWithTrailers();
        }

        private Expression ParseStarExpression() {
            if (TryRead(TokenKind.Multiply)) {
                var start = Current.Span.Start;
                var ws = ReadWhitespace();
                var expr = ParseExpr();
                expr.BeforeNode = ws;
                expr.Freeze();
                return new StarredExpression(expr) {
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

                    expr = new BinaryExpression {
                        Left = expr,
                        Operator = op,
                        Right = rhs,
                        Comment = ReadComment(),
                        AfterNode = ReadWhitespace()
                    };
                }
            }
            return expr;
        }

        private Expression ParseFactor() {
            UnaryExpression topExpr = null, unaryExpr = null;
            AwaitExpression awaitExpr = null;

            while (PeekNonWhitespace.IsAny(TokenUsage.UnaryOperator, TokenUsage.BinaryOrUnaryOperator)) {
                var expr = new UnaryExpression();
                switch (Next().Kind) {
                    case TokenKind.Add:
                        expr.Operator = PythonOperator.Add;
                        break;
                    case TokenKind.Subtract:
                        expr.Operator = PythonOperator.Negate;
                        break;
                    case TokenKind.Twiddle:
                        expr.Operator = PythonOperator.Invert;
                        break;
                    default:
                        ThrowError();
                        return null;
                }
                expr.Comment = ReadComment();
                expr.AfterNode = ReadWhitespace();
                if (unaryExpr == null) {
                    topExpr = unaryExpr = expr;
                } else {
                    unaryExpr.Expression = expr;
                    unaryExpr = expr;
                }
            }

            if (HasAsyncAwait && IsInAsyncFunction && PeekNonWhitespace.Is(TokenKind.KeywordAwait)) {
                awaitExpr = new AwaitExpression {
                    Span = Read(TokenKind.KeywordAwait),
                };
                if (unaryExpr != null) {
                    awaitExpr.Expression = topExpr;
                    unaryExpr.Expression = ParsePower();
                } else {
                    awaitExpr.Expression = ParsePower();
                }
                awaitExpr.Comment = ReadComment();
                awaitExpr.AfterNode = ReadWhitespace();
                return awaitExpr;
            }

            if (unaryExpr != null) {
                unaryExpr.Expression = ParsePower();
                return topExpr;
            }

            return ParsePower();
        }

        private Expression ParsePower() {
            var expr = ParsePrimaryWithTrailers();
            if (TryRead(TokenKind.Power)) {
                var ws = ReadWhitespace();
                var rhs = ParseFactor();
                rhs.BeforeNode = ws;
                rhs.Freeze();

                expr = new BinaryExpression {
                    Left = expr,
                    Operator = PythonOperator.Power,
                    Right = rhs,
                    Comment = ReadComment(),
                    AfterNode = ReadWhitespace()
                };
            }
            return expr;
        }

        private Expression ParsePrimaryWithTrailers() {
            var expr = ParseGroup();
            // TODO: trailers
            return expr;
        }

        private Expression ParseGroup() {
            if (!Peek.Is(TokenUsage.BeginGroup)) {
                return ParsePrimary();
            }

            var start = Current.Span.Start;

            switch (Peek.Kind) {
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

                default:
                    Debug.Fail("Unhandled group: " + Peek.Kind);
                    return new ErrorExpression {
                        Span = Peek.Span
                    };
            }
        }

        private Expression ParsePrimary() {
            if (!Peek.Is(TokenUsage.Primary)) {
                if (!HasAsyncAwait && Peek.IsAny(TokenKind.KeywordAsync, TokenKind.KeywordAwait)) {
                    // pass
                } else if (!HasWith && Peek.Is(TokenKind.KeywordWith)) {
                    // pass
                } else if (HasPrintFunction && Peek.Is(TokenKind.KeywordPrint)) {
                    // pass
                } else if (Peek.IsAny(TokenUsage.EndGroup, TokenUsage.EndStatement)) {
                    return new EmptyExpression {
                        Span = new SourceSpan(Peek.Span.Start, Peek.Span.Start)
                    };
                } else {
                    ThrowError();
                    return null;
                }
            }

            switch (Peek.Kind) {
                case TokenKind.Name:
                // If we've maked it this far, these keywords should be treated
                // as regular names.
                case TokenKind.KeywordAsync:
                case TokenKind.KeywordAwait:
                case TokenKind.KeywordWith:
                    return new NameExpression(_tokenization.GetTokenText(Next())) {
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

                case TokenKind.LiteralDecimal:
                    BigInteger biValue;
                    if (!BigInteger.TryParse(_tokenization.GetTokenText(Next()), out biValue)) {
                        ThrowError();
                    }
                    return new ConstantExpression {
                        Span = Current.Span,
                        Value = (int.MinValue < biValue && biValue < int.MaxValue) ?
                            (object)(int)biValue :
                            (object)biValue,
                        Comment = ReadComment(),
                        AfterNode = ReadWhitespace()
                    };
                default:
                    return new ErrorExpression {
                        Span = Peek.Span,
                        Comment = ReadComment(),
                        AfterNode = ReadWhitespace()
                    };
            }
        }

        private Expression ParseStringLiteral() {
            var span = Current.Span;

            Expression expr;
            var parts = new List<Expression>();
            var open = Next();

            while (open.Kind.GetUsage() == TokenUsage.BeginGroup) {
                var quoteStart = Current.Span.Start;

                SourceLocation? start = null, end = null;
                string value = string.Empty;

                while (TryRead(TokenKind.LiteralString)) {
                    start = start ?? Current.Span.Start;
                    end = Current.Span.End;
                    value += _tokenization.GetTokenText(Current);
                }

                if (start.HasValue && end.HasValue) {
                    expr = new ConstantExpression { Value = value };
                } else {
                    expr = new EmptyExpression { };
                }

                Read(open.Kind.GetGroupEnding());
                expr.Span = new SourceSpan(quoteStart, Current.Span.End);
                expr.Comment = ReadComment();
                expr.AfterNode = ReadWhitespace();
                expr.Freeze();

                parts.Add(expr);
            }

            return new StringExpression {
                Parts = parts,
                Span = new SourceSpan(parts.First().Span.Start, parts.Last().Span.End),
                Comment = ReadComment(),
                AfterNode = ReadWhitespace()
            };
        }

        private Expression ParseListLiteralOrComprehension() {
            var start = Read(TokenKind.LeftBracket).Start;

            var list = new ListExpression();
            if (TryRead(TokenKind.RightBracket)) {
                list.Span = new SourceSpan(start, Current.Span.End);
                list.Comment = ReadComment();
                list.AfterNode = ReadWhitespace();
                return list;
            }

            while (true) {
                var expr = ParseSingleExpression();
                list.AddItem(expr);

                if (TryRead(TokenKind.RightBracket)) {
                    list.Span = new SourceSpan(start, Current.Span.End);
                    list.Comment = ReadComment();
                    list.AfterNode = ReadWhitespace();
                    return list;
                }

                if (list.Items.Count == 1 && TryRead(TokenKind.KeywordFor)) {
                    return new ListComprehension {
                        Item = expr,
                        Iterators = ReadComprehension(),
                        Span = new SourceSpan(start, Read(TokenKind.RightBracket).End),
                        Comment = ReadComment(),
                        AfterNode = ReadWhitespace()
                    };
                }

                Read(TokenKind.Comma);
            }
        }

        private Expression ParseDictOrSetLiteralOrComprehension() {
            return null;
        }

        private Expression ParseDictLiteralOrComprehension() {
            return null;
        }

        private Expression ParseSetLiteralOrComprehension() {
            return null;
        }

        private List<ComprehensionIterator> ReadComprehension() {
            return null;
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

            while (Peek.IsAny(TokenKind.Whitespace, TokenKind.ExplicitLineJoin, TokenKind.SemiColon) ||
                Current.Is(TokenKind.ExplicitLineJoin) && Peek.Is(TokenKind.NewLine)
            ) {
                end = Next().Span.End;
            }

            return new SourceSpan(start, end);
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

        private NameExpression ReadName(string error = "invalid syntax", SourceLocation? errorAt = null) {
            var before = ReadWhitespace();

            string name;
            Next();
            switch (Current.Kind) {
                case TokenKind.Name:
                    name = _tokenization.GetTokenText(Current);
                    break;
                case TokenKind.KeywordAsync:
                case TokenKind.KeywordAwait:
                    if (HasAsyncAwait && IsInAsyncFunction) {
                        ThrowError(error, errorAt);
                        return null;
                    }
                    name = Current.Is(TokenKind.KeywordAsync) ? "async" : "await";
                    break;
                default:
                    ThrowError(error, errorAt);
                    return null;
            }

            var expr = new NameExpression(name) {
                BeforeNode = before,
                Span = Current.Span,
                AfterNode = ReadWhitespace()
            };

            expr.Freeze();
            return expr;
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

        private void ReportError(string error = "invalid syntax", SourceSpan? errorAt = null) {
            _errors.Add(error, errorAt ?? Peek.Span, 0, Severity.Error);
        }

        private void ReportWarning(string error = "invalid syntax", SourceSpan? errorAt = null) {
            _errors.Add(error, errorAt ?? Peek.Span, 0, Severity.Warning);
        }

        private void ThrowError(string error = "invalid syntax", SourceLocation? errorAt = null) {
            throw new ParseErrorException(error, errorAt ?? Peek.Span.Start);
        }

        #endregion
    }
}
