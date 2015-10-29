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
            var indent = Peek.Is(TokenKind.SignificantWhitespace) ? Peek.Span : SourceSpan.None;
            _currentIndent = indent;
            _singleLine = !assumeMultiLine && !Current.Is(TokenKind.NewLine);

            while (ReadCurrentIndent()) {
                // Leading whitespace already read; it will be attached to the
                // suite rather than each statement.
                body.Add(ParseStmt());
            }

            _currentIndent = prevIndent;
            _singleLine = prevSingleLine;

            var suite = new SuiteStatement(body, indent) {
                Comment = ReadComment(),
                AfterNode = ReadWhitespaceAndNewline()
            };
            suite.Freeze();
            return suite;
        }

        private bool ReadCurrentIndent() {
            Debug.Assert(_singleLine || _currentIndent.Length == 0 || Current.Is(TokenUsage.EndStatement));
            if (Peek.Is(TokenKind.EndOfFile)) {
                return false;
            }

            if (_singleLine) {
                if (Peek.Is(TokenKind.NewLine)) {
                    return false;
                }
                return true;
            }

            if (_currentIndent.Length == 0) {
                if (Peek.Is(TokenKind.SignificantWhitespace)) {
                    ThrowError("unexpected indent");
                    return false;
                }
                return true;
            }

            if (Peek.Is(TokenKind.SignificantWhitespace)) {
                // TODO: Proper indent matching
                var indent = _tokenization.GetTokenText(_currentIndent);
                var here = _tokenization.GetTokenText(Peek);
                if (here == indent) {
                    Next();
                    return true;
                } else {
                    ThrowError("unexpected indent");
                    return false;
                }
            }

            if (Peek.Is(TokenCategory.Whitespace) || Peek.Is(TokenKind.Comment)) {
                return true;
            }

            return false;
        }


        private Statement ParseStmt() {
            var ws = ReadWhitespace();
            Statement stmt = null;

            var firstSpan = Peek.Span;

            try {
                switch (Peek.Kind.GetUsage()) {
                    case TokenUsage.EndStatement:
                        stmt = new EmptyStatement {
                            Comment = ReadComment(),
                            AfterNode = ReadWhitespaceAndNewline()
                        };
                        break;
                    case TokenUsage.BeginStatement:
                    case TokenUsage.BeginStatementOrBinaryOperator:
                        stmt = ParseIdentifierAsStatement();
                        break;
                    default:
                        stmt = ParseExprStmt();
                        break;
                }
            } catch (ParseErrorException ex) {
                ReadUntil(IsEndOfStatement);
                stmt = new ErrorStatement(ex.Message) {
                    Span = new SourceSpan(firstSpan.Start, Current.Span.Start)
                };
            }

            //Debug.Assert(Peek.Is(TokenUsage.EndStatement), Peek.Kind.ToString());
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
                    return ParseWithStmt(isAsync);
                case TokenKind.KeywordAsync:
                    return ParseAsyncStmt();
                case TokenKind.KeywordPrint:
                    if (_version.Is2x()) {
                        return ParsePrintStmt();
                    }
                    goto default;
                case TokenKind.KeywordPass:
                    return new PassStatement {
                        Span = Next().Span,
                        Comment = ReadComment(),
                        AfterNode = ReadWhitespaceAndNewline()
                    };
                case TokenKind.KeywordBreak:
                    return ParseBreakStmt();
                case TokenKind.KeywordContinue:
                    return ParseContinueStmt();
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

        private Statement ParseIfStmt() {
            var stmt = new IfStatement() {
                Span = Read(TokenKind.KeywordIf)
            };

            while (true) {
                var test = new IfStatementTest(Current.Kind) { Span = Current.Span };
                test.Test = ParseExpression();
                Read(TokenKind.Colon);
                test.Comment = ReadComment();
                test.AfterComment = ReadWhitespaceAndNewline();
                test.Body = ParseSuite();
                test.Freeze();
                stmt.AddTest(test);

                if (!ReadCurrentIndent()) {
                    break;
                }

                if (!TryRead(TokenKind.KeywordElseIf)) {
                    break;
                }
            }

            if (TryRead(TokenKind.KeywordElse)) {
                var test = new IfStatementTest(TokenKind.KeywordElse) { Span = Current.Span };
                test.Test = new EmptyExpression { AfterNode = ReadWhitespace() };
                Read(TokenKind.Colon);
                test.Comment = ReadComment();
                test.AfterComment = ReadWhitespaceAndNewline();
                test.Body = ParseSuite();
                test.Freeze();
                stmt.ElseStatement = test;
            }

            stmt.Span = new SourceSpan(stmt.Span.Start, Current.Span.Start);
            stmt.Freeze();
            return stmt;
        }

        private Statement ParseWhileStmt() {
            var stmt = new WhileStatement() {
                Span = Read(TokenKind.KeywordWhile),
                Test = ParseExpression()
            };

            Read(TokenKind.Colon);
            stmt.Comment = ReadComment();
            stmt.AfterComment = ReadWhitespaceAndNewline();
            stmt.Body = ParseSuite();

            if (ReadCurrentIndent() && TryRead(TokenKind.KeywordElse)) {
                stmt.BeforeElseColon = ReadWhitespace();
                Read(TokenKind.Colon);
                stmt.ElseComment = ReadComment();
                stmt.AfterElseComment = ReadWhitespaceAndNewline();
                stmt.Else = ParseSuite();
            }

            stmt.Span = new SourceSpan(stmt.Span.Start, Current.Span.Start);
            stmt.Freeze();
            return stmt;
        }

        private Statement ParseForStmt(bool isAsync) {
            var stmt = new ForStatement(isAsync);
            if (isAsync) {
                stmt.Span = new SourceSpan(Read(TokenKind.KeywordAsync).Start, Read(TokenKind.KeywordFor).End);
            } else {
                stmt.Span = Read(TokenKind.KeywordFor);
            }

            stmt.Index = ParseAssignmentTarget();
            Read(TokenKind.KeywordIn);
            stmt.List = ParseExpression();

            Read(TokenKind.Colon);
            stmt.Comment = ReadComment();
            stmt.AfterComment = ReadWhitespaceAndNewline();
            stmt.Body = ParseSuite();

            if (ReadCurrentIndent() && TryRead(TokenKind.KeywordElse)) {
                stmt.BeforeElseColon = ReadWhitespace();
                Read(TokenKind.Colon);
                stmt.ElseComment = ReadComment();
                stmt.AfterElseComment = ReadWhitespaceAndNewline();
                stmt.Else = ParseSuite();
            }

            stmt.Span = new SourceSpan(stmt.Span.Start, Current.Span.Start);
            stmt.Freeze();
            return stmt;
        }

        private Statement ParseTryStmt() {
            return null;
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
            return null;
        }

        private Statement ParseAsyncStmt() {
            return null;
        }

        private Statement ParsePrintStmt() {
            return null;
        }

        private Statement ParseBreakStmt() {
            var stmt = new BreakStatement {
                BeforeNode = ReadWhitespace(),
                Span = Read(TokenKind.KeywordBreak),
                Comment = ReadComment(),
                AfterNode = ReadWhitespaceAndNewline()
            };
            stmt.Freeze();
            return stmt;
        }

        private Statement ParseContinueStmt() {
            var stmt = new ContinueStatement {
                BeforeNode = ReadWhitespace(),
                Span = Read(TokenKind.KeywordContinue),
                Comment = ReadComment(),
                AfterNode = ReadWhitespaceAndNewline()
            };
            stmt.Freeze();
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
            return new ExpressionStatement(ParseExpression());
        }


        private Expression ParseAssignmentTarget() {
            var expr = ParseStarExpression();
            expr.Freeze();
            return expr;
        }

        private Expression ParseExpression() {
            if (PeekNonWhitespace.Is(TokenKind.KeywordLambda)) {
                return ParseLambda();
            }

            var ws = ReadWhitespace();
            var expr = ParseOrTest();

            expr.BeforeNode = ws;
            ReadCommentAndWhitespace(expr);
            expr.Freeze();
            return expr;
        }

        private Expression ParseLambda() {
            var expr = new LambdaExpression {
                BeforeNode = ReadWhitespace(),
                Span = Read(TokenKind.KeywordLambda),
                Parameters = ParseParameterList(),
                BeforeColon = ReadWhitespace(),
            };

            Read(TokenKind.Colon, errorAt: expr.Span.End);


            expr.Expression = ParseExpression();
            return expr;
        }

        private ParameterList ParseParameterList(bool forLambda = false) {
            var parameters = new ParameterList {
                BeforeNode = ReadWhitespace()
            };

            while (true) {
                if (forLambda && Current.Is(TokenKind.Colon)) {
                    break;
                }

                if (!forLambda && Current.Is(TokenKind.RightParenthesis)) {
                    break;
                }

                var p = new Parameter();

                if (TryRead(TokenKind.Multiply)) {
                    p.Kind = ParameterKind.List;
                } else if (TryRead(TokenKind.Power)) {
                    p.Kind = ParameterKind.Dictionary;
                }

                p.NameExpression = ReadName();

                if (!forLambda && HasAnnotations && TryRead(TokenKind.Colon)) {
                    p.Annotation = ParseExpression();
                }

                if (TryRead(TokenKind.Assign)) {
                    p.DefaultValue = ParseExpression();
                }

                if (Current.Kind == TokenKind.Comma) {
                    var comma = Current.Span;
                    var ws = ReadWhitespace();
                    if (ws.End.Index > comma.End.Index) {
                        p.AfterNode = new SourceSpan(comma.Start, ws.End);
                    } else {
                        p.AfterNode = comma;
                    }
                    p.Comment = ReadComment();
                    if (p.Comment != null) {
                        p.Comment.BeforeNode = p.AfterNode;
                        p.AfterNode = ReadWhitespace();
                    }
                    p.Freeze();
                } else {
                    ReadCommentAndWhitespace(p);
                    p.Freeze();
                }

                parameters.AddParameter(p);
            }

            ReadCommentAndWhitespace(parameters);
            parameters.Freeze();
            return parameters;
        }

        private Expression ParseOrTest() {
            var expr = ParseAndTest();
            if (TryRead(TokenKind.KeywordOr)) {
            }
            return expr;
        }

        private Expression ParseAndTest() {
            var expr = ParseNotTest();
            if (TryRead(TokenKind.KeywordAnd)) {
            }
            return expr;
        }

        private Expression ParseNotTest() {
            return TryRead(TokenKind.KeywordNot) ?
                new UnaryExpression { Operator = PythonOperator.Not, Expression = ParseNotTest() } :
                ParseComparison();
        }

        private Expression ParseComparison() {
            var expr = ParseStarExpression();
            if (Current.Is(TokenUsage.Comparison)) {
                switch (Current.Kind) {
                    case TokenKind.Equals:
                        break;
                    default:
                        return expr;
                }
                Next();
            }
            return expr;
        }

        private Expression ParseStarExpression() {
            return TryRead(TokenKind.Multiply) ?
                new StarredExpression(ParseExpr()) :
                ParseExpr();
        }

        private Expression ParseExpr() {
            var expr = ParseFactor();
            while (Peek.IsAny(TokenUsage.BinaryOperator, TokenUsage.BinaryOrUnaryOperator)) {

                Next();
            }
            return expr;
        }

        private Expression ParseFactor() {
            UnaryExpression topExpr = null, unaryExpr = null;
            AwaitExpression awaitExpr = null;

            while (PeekNonWhitespace.IsAny(TokenUsage.UnaryOperator, TokenUsage.BinaryOrUnaryOperator)) {
                var expr = new UnaryExpression {
                    BeforeNode = ReadWhitespace()
                };
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
                ReadCommentAndWhitespace(expr);
                if (unaryExpr == null) {
                    topExpr = unaryExpr = expr;
                } else {
                    unaryExpr.Expression = expr;
                    unaryExpr = expr;
                }
            }

            if (HasAsyncAwait && IsInAsyncFunction && PeekNonWhitespace.Is(TokenKind.KeywordAwait)) {
                awaitExpr = new AwaitExpression {
                    BeforeNode = ReadWhitespace(),
                    Span = Read(TokenKind.KeywordAwait),
                };
                if (unaryExpr != null) {
                    awaitExpr.Expression = topExpr;
                    unaryExpr.Expression = ParsePower();
                } else {
                    awaitExpr.Expression = ParsePower();
                }
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
                expr = new BinaryExpression {
                    Left = expr,
                    Operator = PythonOperator.Power,
                    Right = ParseFactor()
                };
            }
            return expr;
        }

        private Expression ParsePrimaryWithTrailers() {
            var expr = ParsePrimary();
            // TODO: trailers
            return expr;
        }

        private Expression ParsePrimary() {
            if (!Peek.Is(TokenUsage.Primary)) {
                ThrowError();
                return null;
            }

            var start = Current.Span.Start;

            switch (Peek.Kind) {
                case TokenKind.Name:
                    return new NameExpression(_tokenization.GetTokenText(Next())) {
                        Span = Current.Span
                    };

                case TokenKind.LeftBracket:
                    return ParseListLiteralOrComprehension();
                case TokenKind.LeftParenthesis:
                    return new ParenthesisExpression {
                        Expression = ParseExpression(),
                        Span = new SourceSpan(start, Current.Span.End)
                    };

                case TokenKind.RightBrace:
                case TokenKind.RightBracket:
                case TokenKind.RightParenthesis:
                    return new EmptyExpression {
                        Span = new SourceSpan(Current.Span.End, Current.Span.End)
                    };

                case TokenKind.LeftSingleQuote:
                case TokenKind.LeftDoubleQuote:
                case TokenKind.LeftSingleTripleQuote:
                case TokenKind.LeftDoubleTripleQuote:
                    return ParseStringLiteral();

                case TokenKind.LiteralString:
                case TokenKind.RightSingleQuote:
                case TokenKind.RightDoubleQuote:
                case TokenKind.RightSingleTripleQuote:
                case TokenKind.RightDoubleTripleQuote:
                    // Should only ever enter this function at OpenQuote
                    ThrowError("unexpected string literal");
                    return null;

                case TokenKind.LiteralDecimal:
                    BigInteger biValue;
                    if (!BigInteger.TryParse(_tokenization.GetTokenText(Next()), out biValue)) {
                        ThrowError();
                    }
                    return new ConstantExpression {
                        Span = Current.Span,
                        Value = (int.MinValue < biValue && biValue < int.MaxValue) ?
                            (object)(int)biValue :
                            (object)biValue
                    };
                default:
                    return new ErrorExpression {
                        Span = Peek.Span
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
                    expr = new EmptyExpression();
                }

                Read(open.Kind.GetGroupEnding());
                expr.Span = new SourceSpan(quoteStart, Current.Span.End);
                ReadCommentAndWhitespace(expr);
                expr.Freeze();

                parts.Add(expr);
            }

            return new StringExpression {
                Parts = parts,
                Span = new SourceSpan(parts.First().Span.Start, parts.Last().Span.End)
            };
        }

        private Expression ParseListLiteralOrComprehension() {
            var start = Read(TokenKind.LeftBracket).Start;

            var list = new ListExpression();
            if (TryRead(TokenKind.RightBracket)) {
                list.Span = new SourceSpan(start, Current.Span.End);
                return list;
            }

            while (true) {
                var expr = ParseExpression();
                list.AddItem(expr);

                if (TryRead(TokenKind.RightBracket)) {
                    list.Span = new SourceSpan(start, Current.Span.End);
                    return list;
                }

                if (list.Items.Count == 1 && TryRead(TokenKind.KeywordFor)) {
                    return new ListComprehension {
                        Item = expr,
                        Iterators = ReadComprehension(),
                        Span = new SourceSpan(start, Read(TokenKind.RightBracket).End)
                    };
                }

                Read(TokenKind.Comma);
            }
        }

        private List<ComprehensionIterator> ReadComprehension() {
            return null;
        }

        #endregion

        #region Read functions

        private SourceSpan ReadWhitespace() {
            SourceLocation? start = null, end = null;

            while (Peek.IsAny(TokenKind.Whitespace, TokenKind.ExplicitLineJoin, TokenKind.SemiColon) ||
                Current.Is(TokenKind.ExplicitLineJoin) && Peek.Is(TokenKind.NewLine)
            ) {
                Next();
                start = start ?? Current.Span.Start;
                end = Current.Span.End;
            }

            return new SourceSpan(start.GetValueOrDefault(), end.GetValueOrDefault());
        }

        private SourceSpan ReadWhitespaceAndNewline() {
            SourceLocation? start = null, end = null;

            while (Peek.Is(TokenCategory.Whitespace) ||
                Peek.Is(TokenUsage.EndStatement) ||
                Peek.Is(TokenKind.ExplicitLineJoin)
            ) {
                Next();
                bool allowBreak = !Current.Is(TokenKind.ExplicitLineJoin) || !Peek.Is(TokenKind.NewLine);
                start = start ?? Current.Span.Start;
                end = Current.Span.End;
                if (allowBreak && Current.Is(TokenUsage.EndStatement)) {
                    break;
                }
            }

            return new SourceSpan(start.GetValueOrDefault(), end.GetValueOrDefault());
        }

        private CommentExpression ReadComment() {
            if (PeekNonWhitespace.Is(TokenCategory.Comment)) {
                return new CommentExpression {
                    BeforeNode = ReadWhitespace(),
                    Span = Next().Span,
                };
            }
            return null;
        }

        private void ReadCommentAndWhitespace(Node target) {
            target.Comment = ReadComment();
            target.AfterNode = ReadWhitespace();
        }

        private void ReadCommentAndNewline(Statement target) {
            target.Comment = ReadComment();
            target.AfterNode = ReadWhitespaceAndNewline();
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

        private void ThrowError(string error = "invalid syntax", SourceLocation? errorAt = null) {
            throw new ParseErrorException(error, errorAt ?? _current.Span.Start);
        }

        #endregion
    }
}
