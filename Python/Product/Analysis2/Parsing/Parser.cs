﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        public Parser(Tokenization tokenization) {
            _tokenization = tokenization;
            _version = _tokenization.LanguageVersion;
        }

        private void Reset() {
            _tokenEnumerator = _tokenization.AllTokens.GetEnumerator();
            _lookahead = new List<Token>();
        }

        #region Language Features

        private bool HasAnnotations => _version >= PythonLanguageVersion.V30;

        private bool HasAsyncAwait => _version >= PythonLanguageVersion.V35;

        private bool IsInAsyncFunction { get; set; }

        private bool IsWhitespaceSignificant { get; set; }

        #endregion

        #region Parsing

        public PythonAst Parse() {

            return new PythonAst(ParseSuite(), _tokenization);
        }

        private SuiteStatement ParseSuite() {
            var body = new List<Statement>();

            if (!IsWhitespaceSignificant) {
                // TODO: Find correct error
                ThrowError();
            }

            var prevIndent = _currentIndent;
            var indent = Peek.Category == TokenCategory.WhiteSpace ? Peek.Span : SourceSpan.None;
            _currentIndent = indent;

            while (ReadCurrentIndent()) {
                // Leading whitespace already read; it will be attached to the
                // suite rather than each statement.
                body.Add(ParseStmt());
            }

            _currentIndent = prevIndent;

            return new SuiteStatement(body, indent);
        }

        private bool ReadCurrentIndent() {
            Debug.Assert(Current.Category == TokenCategory.EndOfLine || Current.Category == TokenCategory.EndOfStream);
            if (Current.Category == TokenCategory.EndOfStream) {
                return false;
            }

            if (Peek.Category == TokenCategory.WhiteSpace) {
                // TODO: Proper indent matching
                if (_tokenization.GetTokenText(Peek) == _tokenization.GetTokenText(_currentIndent)) {
                    Next();
                    return true;
                }
            }

            if (Peek.Category == TokenCategory.EndOfLine || Peek.Category == TokenCategory.Comment) {
                return true;
            }

            return false;
        }


        private Statement ParseStmt() {
            var ws = ReadWhitespace();
            Statement stmt = null;

            var firstSpan = Current.Span;

            try {
                switch (Current.Category) {
                    case TokenCategory.EndOfLine:
                    case TokenCategory.EndOfStream:
                    case TokenCategory.SemiColon:
                        stmt = new EmptyStatement();
                        break;
                    case TokenCategory.Comment:
                        stmt = new EmptyStatement {
                            Comment = ReadComment()
                        };
                        break;
                    case TokenCategory.Identifier:
                        stmt = ParseIdentifierAsStatement(Current.GetTokenKind(_tokenization));
                        break;
                    case TokenCategory.BinaryIntegerLiteral:
                    case TokenCategory.DecimalIntegerLiteral:
                    case TokenCategory.FloatingPointLiteral:
                    case TokenCategory.HexadecimalIntegerLiteral:
                    case TokenCategory.ImaginaryLiteral:
                    case TokenCategory.OctalIntegerLiteral:
                    case TokenCategory.OpenGrouping:
                    case TokenCategory.OpenQuote:
                    case TokenCategory.StringLiteral:
                        stmt = ParseExprStmt();
                        break;
                }
            } catch (ParseErrorException ex) {
                ReadUntil(IsEndOfStatement);
                stmt = new ErrorStatement(ex.Message) {
                    Span = new SourceSpan(firstSpan.Start, Current.Span.Start)
                };
            }

            Debug.Assert(Current.Category == TokenCategory.EndOfLine ||
                Current.Category == TokenCategory.EndOfStream ||
                Current.Category == TokenCategory.SemiColon, Current.Category.ToString());
            stmt.AfterNode = Current.Span;
            return stmt;
        }

        private Statement ParseIdentifierAsStatement(TokenKind kind) {
            switch (kind) {
                case TokenKind.KeywordIf:
                    return ParseIfStmt();
                case TokenKind.KeywordWhile:
                    return ParseWhileStmt();
                case TokenKind.KeywordFor:
                    return ParseForStmt(isAsync: false);
                case TokenKind.KeywordTry:
                    return ParseTryStmt();
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
                case TokenKind.KeywordPrint:
                    if (_version.Is2x()) {
                        return ParsePrintStmt();
                    }
                    goto default;
                case TokenKind.KeywordPass:
                    return new PassStatement { Span = Current.Span };
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
            var stmt = new IfStatement();
            Read(TokenKind.KeywordIf);

            while (true) {
                var test = new IfStatementTest { Span = Current.Span };
                test.Test = ParseExpression();
                test.BeforeColon = ReadWhitespace();
                test.Comment = ReadComment();
                test.AfterComment = ReadWhitespace();
                test.Body = ParseSuite();
                test.AfterNode = ReadWhitespace();
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
            }

            stmt.Freeze();
            return stmt;
        }

        private Statement ParseWhileStmt() {
        }

        private Statement ParseForStmt(bool isAsync) {
        }

        private Statement ParseTryStmt() {
        }

        private Statement ParseDecorated() {
        }

        private Statement ParseFuncDef(bool isCoroutine) {
        }

        private Statement ParseClassDef() {
        }

        private Statement ParseWithStmt(bool isAsync) {
        }

        private Statement ParseAsyncStmt() {
        }

        private Statement ParsePrintStmt() {
        }

        private Statement ParseBreakStmt() {
        }

        private Statement ParseContinueStmt() {
        }

        private Statement ParseReturnStmt() {
        }

        private Statement ParseFromImportStmt() {
        }

        private Statement ParseImportStmt() {
        }

        private Statement ParseGlobalStmt() {
        }

        private Statement ParseNonlocalStmt() {
        }

        private Statement ParseRaiseStmt() {
        }

        private Statement ParseAssertStmt() {
        }

        private Statement ParseExecStmt() {
        }

        private Statement ParseDelStmt() {
        }

        private Statement ParseYieldStmt() {
        }

        private Statement ParseExprStmt() {
            return new ExpressionStatement(ParseExpression());
        }


        private Expression ParseExpression() {
            var expr = PeekNonWhitespaceKind == TokenKind.KeywordLambda ?
                ParseLambda() :
                ParseOrTest();

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

            Read(TokenCategory.Colon, errorAt: expr.Span.End);


            expr.Expression = ParseExpression();
            return expr;
        }

        private ParameterList ParseParameterList(bool forLambda = false) {
            var parameters = new ParameterList {
                BeforeNode = ReadWhitespace()
            };

            while (true) {
                if (forLambda && Current.Category == TokenCategory.Colon) {
                    break;
                }

                if (!forLambda && CurrentKind == TokenKind.RightParenthesis) {
                    break;
                }

                var p = new Parameter();

                if (Current.Category == TokenCategory.Operator) {
                    switch (CurrentKind) {
                        case TokenKind.Multiply:
                            p.Kind = ParameterKind.List;
                            break;
                        case TokenKind.Power:
                            p.Kind = ParameterKind.Dictionary;
                            break;
                        default:
                            ThrowError();
                            break;
                    }
                    Next();
                }

                p.NameExpression = ReadName();

                if (!forLambda && HasAnnotations && TryRead(TokenCategory.Colon)) {
                    p.Annotation = ParseExpression();
                }

                if (TryRead(TokenKind.Assign)) {
                    p.DefaultValue = ParseExpression();
                }

                if (Current.Category == TokenCategory.Comma) {
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
            if (CurrentKind == TokenKind.KeywordOr) {
            }
            return expr;
        }

        private Expression ParseAndTest() {
            var expr = ParseNotTest();
            if (CurrentKind == TokenKind.KeywordAnd) {
            }
            return expr;
        }

        private Expression ParseNotTest() {
            return TryRead(TokenKind.KeywordNot) ?
                new UnaryExpression(PythonOperator.Not, ParseNotTest()) :
                ParseComparison();
        }

        private Expression ParseComparison() {
            var expr = ParseStarExpression();
            if (Current.Category == TokenCategory.Operator) {
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
            while (Current.Category == TokenCategory.Operator) {

            }
            return expr;
        }

        private Expression ParseFactor() {
            if (Current.Category == TokenCategory.Operator) {
                switch (CurrentKind) {
                    case TokenKind.Add:
                        Next();
                        return new UnaryExpression(PythonOperator.Add, ParseFactor());
                    case TokenKind.Subtract:
                        Next();
                        return new UnaryExpression(PythonOperator.Negate, ParseFactor());
                    case TokenKind.Twiddle:
                        Next();
                        return new UnaryExpression(PythonOperator.Invert, ParseFactor());
                }
            }
            return ParseAwaitExpr();
        }

        private Expression ParseAwaitExpr() {
            if (HasAsyncAwait && IsInAsyncFunction) {
            }
            return ParsePower();
        }

        private Expression ParsePower() {
            var expr = ParsePrimaryWithTrailers();
            if (TryRead(TokenKind.Power)) {
                expr = new BinaryExpression(PythonOperator.Power, expr, ParseFactor());
            }
            return expr;
        }

        private Expression ParsePrimaryWithTrailers() {
            var expr = ParsePrimary();
            // TODO: trailers
            return expr;
        }

        private Expression ParsePrimary() {
            switch (CurrentKind) {
                // TODO: kinds
                default:
                    ThrowError();
                    return null;
            }
        }

        #endregion

        #region Read functions

        private SourceSpan ReadWhitespace() {
            SourceLocation? start = null, end = null;

            bool includeEndOfLine = false;

            while (Peek.Category == TokenCategory.WhiteSpace ||
                Peek.Category == TokenCategory.IgnoreEndOfLine ||
                (!IsWhitespaceSignificant || includeEndOfLine) && Peek.Category == TokenCategory.EndOfLine) {
                Next();
                start = start ?? Current.Span.Start;
                end = Current.Span.End;
                includeEndOfLine = (Current.Category == TokenCategory.IgnoreEndOfLine);
            }

            return new SourceSpan(start.GetValueOrDefault(), end.GetValueOrDefault());
        }

        private CommentExpression ReadComment() {
            if (PeekNonWhitespace.Category == TokenCategory.Comment) {
                return new CommentExpression {
                    BeforeNode = ReadWhitespace(),
                    Span = Read(TokenCategory.Comment),
                };
            }
            return null;
        }

        private void ReadCommentAndWhitespace(Node target) {
            target.Comment = ReadComment();
            target.AfterNode = ReadWhitespace();
        }

        private NameExpression ReadName(string error = "invalid syntax", SourceLocation? errorAt = null) {
            var before = ReadWhitespace();
            Read(TokenCategory.Identifier, error, errorAt);

            var kind = CurrentKind;
            var name = new NameExpression(_tokenization.GetTokenText(Current)) {
                BeforeNode = before,
                Span = Current.Span,
                AfterNode = ReadWhitespace()
            };

            switch (kind) {
                case TokenKind.Name:
                    break;
                case TokenKind.KeywordAsync:
                case TokenKind.KeywordAwait:
                    if (HasAsyncAwait && IsInAsyncFunction) {
                        ThrowError(error, name.Span.End);
                    }
                    break;
                default:
                    ThrowError(error, name.Span.End);
                    break;
            }

            name.Freeze();
            return name;
        }

        private bool TryRead(TokenCategory category) {
            if (Peek.Category == category) {
                Next();
                return true;
            }
            return false;
        }

        private bool TryRead(TokenKind kind) {
            if (Peek.GetTokenKind(_tokenization) == kind) {
                Next();
                return true;
            }
            return false;
        }

        private SourceSpan Read(
            TokenCategory category,
            string error = "invalid syntax",
            SourceLocation? errorAt = null
        ) {
            if (!TryRead(category)) {
                ThrowError(error, errorAt);
            }
            return Current.Span;
        }

        private SourceSpan Read(TokenKind kind, string error = "invalid syntax", SourceLocation? errorAt = null) {
            if (!TryRead(kind)) {
                ThrowError(error, errorAt);
            }
            return Current.Span;
        }

        private void ReadUntil(Func<Token, bool> predicate) {
            while (Peek.Category != TokenCategory.EndOfStream && !predicate(Peek)) {
                Next();
            }
        }

        #endregion

        #region Token Predicates

        private static bool IsEndOfStatement(Token token) {
            return token.Category == TokenCategory.EndOfLine ||
                token.Category == TokenCategory.EndOfStream ||
                token.Category == TokenCategory.SemiColon;
        }

        #endregion

        #region Token Scanning

        private Token Current {
            get {
                return _current;
            }
        }

        private TokenKind CurrentKind {
            get {
                // TODO: Cache for current token
                return _current.GetTokenKind(_tokenization);
            }
        }

        private void FillLookahead(int tokenCount) {
            if (_lookahead == null) {
                return;
            }

            while (_lookahead.Count < tokenCount) {
                if (_tokenEnumerator.MoveNext()) {
                    _lookahead.Add(_tokenEnumerator.Current);
                } else {
                    _lookahead.Add(Token.EOF);
                }
            }
        }

        private void PushCurrent(Token token) {
            if (_lookahead != null) {
                _lookahead.Insert(0, _current);
            } else {
                _lookahead = new List<Token> { _current, Token.EOF };
            }
            _current = token;
        }

        private Token Next() {
            if (_lookahead == null) {
                return Token.EOF;
            }
            FillLookahead(1);
            _current = _lookahead[0];
            _lookahead.RemoveAt(0);
            if (_current.Equals(Token.EOF) && _lookahead.Count == 0) {
                _lookahead = null;
            }
            return _current;
        }

        private Token Peek {
            get {
                FillLookahead(1);
                return _lookahead[0];
            }
        }

        private Token PeekNonWhitespace {
            get {
                FillLookahead(2);
                int i = 0;
                while (true) {
                    if (_lookahead[i].Category == TokenCategory.EndOfStream) {
                        return _lookahead[i];
                    } else if (_lookahead[i].Category == TokenCategory.IgnoreEndOfLine) {
                        FillLookahead(i + 3);
                        if (_lookahead[i + 1].Category == TokenCategory.EndOfLine) {
                            i += 2;
                        } else {
                            return _lookahead[i];
                        }
                    } else if (_lookahead[i].Category == TokenCategory.WhiteSpace) {
                        i += 1;
                    } else {
                        return _lookahead[i];
                    }
                    FillLookahead(i + 2);
                }
            }
        }

        private TokenKind PeekNonWhitespaceKind {
            get {
                return PeekNonWhitespace.GetTokenKind(_tokenization);
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
