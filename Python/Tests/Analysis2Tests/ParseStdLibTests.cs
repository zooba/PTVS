using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestUtilities;

namespace Analysis2Tests {
    [TestClass]
    [Ignore]
    public abstract class ParseStdLibTests {
        public abstract InterpreterConfiguration Configuration {
            get;
        }

        [TestInitialize]
        public void EnsureConfiguration() {
            if (Configuration == null) {
                Assert.Inconclusive("Configuration for " + GetType().Name + " not avaliable.");
            }
        }

        private async Task<string> ParseOneFile(string path) {
            var doc = new FileSourceDocument(path);
            var tok = await Tokenization.TokenizeAsync(doc, Configuration.Version);
            var parser = new Parser(tok);
            var errors = new CollectingErrorSink();

            CancellationToken cancel;
            if (Debugger.IsAttached) {
                cancel = CancellationToken.None;
            } else {
                cancel = new CancellationTokenSource(5000).Token;
            }

            var ast = await Task.Run(() => parser.Parse(errors), cancel);

            var sb = new StringBuilder();
            sb.AppendLine(path);

            sb.AppendLine("Parse Errors");
            foreach (var error in errors.Errors) {
                sb.AppendLine(error.ToString());
            }

            sb.AppendLine();
            sb.AppendLine("AST Errors");
            var errorWalker = new ErrorWalker(tok, sb);
            ast.Walk(errorWalker);

            if (errors.Errors.Any() || errorWalker.Any) {
                return sb.ToString();
            }
            return null;
        }

        [TestMethod, Priority(0)]
        public async Task ArgParseModule() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "argparse.py");
            Assert.IsTrue(File.Exists(file), "Cannot find " + file);
            var text = await ParseOneFile(file);
            if (!string.IsNullOrEmpty(text)) {
                Assert.Fail(text);
            }
        }

        [TestMethod, Priority(0)]
        public async Task CgiModule() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "cgi.py");
            Assert.IsTrue(File.Exists(file), "Cannot find " + file);
            var text = await ParseOneFile(file);
            if (!string.IsNullOrEmpty(text)) {
                Assert.Fail(text);
            }
        }

        [TestMethod, Priority(0)]
        public async Task OSModule() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "os.py");
            Assert.IsTrue(File.Exists(file), "Cannot find " + file);
            var text = await ParseOneFile(file);
            if (!string.IsNullOrEmpty(text)) {
                Assert.Fail(text);
            }
        }

        [TestMethod, Priority(0)]
        public async Task PlatformModule() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "platform.py");
            Assert.IsTrue(File.Exists(file), "Cannot find " + file);
            var text = await ParseOneFile(file);
            if (!string.IsNullOrEmpty(text)) {
                Assert.Fail(text);
            }
        }

        [TestMethod, Priority(0)]
        public async Task SocketModule() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "socket.py");
            Assert.IsTrue(File.Exists(file), "Cannot find " + file);
            var text = await ParseOneFile(file);
            if (!string.IsNullOrEmpty(text)) {
                Assert.Fail(text);
            }
        }


        [TestMethod]
        public async Task TopLevelStdLib() {
            var dir = Path.Combine(Configuration.PrefixPath, "Lib");
            Assert.IsTrue(Directory.Exists(dir), "Cannot find " + dir);

            var output = new List<string>();

            foreach (var file in Directory.EnumerateFiles(dir, "*.py", SearchOption.TopDirectoryOnly)) {
                var error = await ParseOneFile(file);
                if (!string.IsNullOrEmpty(error)) {
                    output.Add(error);
                    break;
                }
            }

            if (output.Any()) {
                foreach (var text in output) {
                    Trace.TraceError(text);
                }
                Assert.Fail("Errors parsing files");
            }
        }

        private class ErrorWalker : PythonWalker {
            private readonly Tokenization _tokenization;
            private readonly StringBuilder _sb;

            public bool Any;

            public ErrorWalker(Tokenization tokenization, StringBuilder sb) {
                _tokenization = tokenization;
                _sb = sb;
            }

            public override bool Walk(ErrorStatement node) {
                _sb.AppendLine($"ErrorStatement at {node.Span}: {_tokenization.GetTokenText(node.Span)}");
                Any = true;
                return base.Walk(node);
            }

            public override bool Walk(ErrorExpression node) {
                _sb.AppendLine($"ErrorExpression at {node.Span}: {_tokenization.GetTokenText(node.Span)}");
                Any = true;
                return base.Walk(node);
            }
        }
    }

    [TestClass]
    public class Parse35StdLib : ParseStdLibTests {
        public override InterpreterConfiguration Configuration {
            get {
                return PythonPaths.Python35?.Configuration ??
                    PythonPaths.Python35_x64?.Configuration;
            }
        }
    }
}
