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

        [ClassInitialize]
        public static void Initialize(TestContext context) {
            AssertListener.Initialize();
        }

        [TestInitialize]
        public void EnsureConfiguration() {
            if (Configuration == null) {
                Assert.Inconclusive("Configuration for " + GetType().Name + " not avaliable.");
            }
        }

        internal async Task<string> ParseOneFile(string path) {
            var doc = new FileSourceDocument(path);
            var tok = await Tokenization.TokenizeAsync(doc, Configuration.Version, CancellationToken.None);
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

        private async Task ParseOnePackage(string path) {
            var output = new List<string>();

            foreach (var file in Directory.EnumerateFiles(path, "*.py", SearchOption.AllDirectories)) {
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
                Assert.Fail("Errors parsing package");
            }
        }

        [TestMethod, Priority(0)]
        public virtual async Task ArgParseModule() {
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
        public async Task SmtplibModule() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "smtplib.py");
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

        [TestMethod, Priority(0)]
        public async Task SslModule() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "ssl.py");
            Assert.IsTrue(File.Exists(file), "Cannot find " + file);
            var text = await ParseOneFile(file);
            if (!string.IsNullOrEmpty(text)) {
                Assert.Fail(text);
            }
        }

        [TestMethod, Priority(0)]
        public async virtual Task TestBuiltinModule() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "test", "test_builtin.py");
            Assert.IsTrue(File.Exists(file), "Cannot find " + file);
            var text = await ParseOneFile(file);
            if (!string.IsNullOrEmpty(text)) {
                Assert.Fail(text);
            }
        }

        [TestMethod, Priority(0)]
        public async virtual Task TestGrammarModule() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "test", "test_grammar.py");
            Assert.IsTrue(File.Exists(file), "Cannot find " + file);
            var text = await ParseOneFile(file);
            if (!string.IsNullOrEmpty(text)) {
                Assert.Fail(text);
            }
        }

        [TestMethod, Priority(1)]
        public virtual async Task CompilerPackage() {
            var path = Path.Combine(Configuration.PrefixPath, "Lib", "compiler");
            Assert.IsTrue(Directory.Exists(path), "Cannot find " + path);
            await ParseOnePackage(path);
        }

        [TestMethod, Priority(1)]
        public async Task CTypesPackage() {
            var path = Path.Combine(Configuration.PrefixPath, "Lib", "ctypes");
            Assert.IsTrue(Directory.Exists(path), "Cannot find " + path);
            await ParseOnePackage(path);
        }

        [TestMethod, Priority(1)]
        public async Task EmailPackage() {
            var path = Path.Combine(Configuration.PrefixPath, "Lib", "email");
            Assert.IsTrue(Directory.Exists(path), "Cannot find " + path);
            await ParseOnePackage(path);
        }


        [TestMethod, Priority(1)]
        public async Task TopLevelStdLib() {
            var dir = Path.Combine(Configuration.PrefixPath, "Lib");
            Assert.IsTrue(Directory.Exists(dir), "Cannot find " + dir);

            var output = new List<string>();

            var tasks = new List<Task<string>>();
            foreach (var file in Directory.EnumerateFiles(dir, "*.py", SearchOption.TopDirectoryOnly)) {
                tasks.Add(ParseOneFile(file));
            }

            var errors = await Task.WhenAll(tasks);
            foreach(var error in errors) {
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

        public abstract IEnumerable<string> SkipFilesInFullStdLibTest { get; }

        [TestMethod, Priority(2)]
        [TestCategory("10s")]
        public async Task FullStdLib() {
            var dir = Path.Combine(Configuration.PrefixPath, "Lib");
            Assert.IsTrue(Directory.Exists(dir), "Cannot find " + dir);

            var output = new List<string>();

            foreach (var file in Directory.EnumerateFiles(dir, "*.py", SearchOption.AllDirectories)) {
                var match = file.Substring(dir.Length);
                if (SkipFilesInFullStdLibTest.Any(f => match.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)) {
                    continue;
                }
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
    public class Parse26StdLib : ParseStdLibTests {
        [Ignore]
        public override async Task ArgParseModule() { }
        [Ignore]
        public override async Task TestBuiltinModule() { }
        [Ignore]
        public override async Task TestGrammarModule() { }

        [TestMethod, Priority(0)]
        public async Task PyDocTopicsModule() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "pydoc_topics.py");
            Assert.IsTrue(File.Exists(file), "Cannot find " + file);
            var text = await ParseOneFile(file);
            if (!string.IsNullOrEmpty(text)) {
                Assert.Fail(text);
            }
        }

        public override InterpreterConfiguration Configuration {
            get {
                return PythonPaths.Python26?.Configuration ??
                    PythonPaths.Python26_x64?.Configuration;
            }
        }

        public override IEnumerable<string> SkipFilesInFullStdLibTest {
            get {
                yield break;
            }
        }
    }

    [TestClass]
    public class Parse27StdLib : ParseStdLibTests {
        [Ignore]
        public override async Task TestBuiltinModule() { }
        [Ignore]
        public override async Task TestGrammarModule() { }

        public override InterpreterConfiguration Configuration {
            get {
                return PythonPaths.Python27?.Configuration ??
                    PythonPaths.Python27_x64?.Configuration;
            }
        }

        public override IEnumerable<string> SkipFilesInFullStdLibTest {
            get {
                yield break;
            }
        }
    }

    [TestClass]
    public class Parse34StdLib : ParseStdLibTests {
        [Ignore]
        public override async Task CompilerPackage() { }

        public override InterpreterConfiguration Configuration {
            get {
                return PythonPaths.Python34?.Configuration ??
                    PythonPaths.Python34_x64?.Configuration;
            }
        }

        [TestMethod, Priority(0)]
        public async virtual Task TestPep3131Module() {
            var file = Path.Combine(Configuration.PrefixPath, "Lib", "test", "test_pep3131.py");
            Assert.IsTrue(File.Exists(file), "Cannot find " + file);
            var text = await ParseOneFile(file);
            if (!string.IsNullOrEmpty(text)) {
                Assert.Fail(text);
            }
        }

        public override IEnumerable<string> SkipFilesInFullStdLibTest {
            get {
                yield return @"\lib2to3\tests\data\";
                yield return @"\test\badsyntax_3131.py";
            }
        }
    }

    [TestClass]
    public class Parse35StdLib : ParseStdLibTests {
        [Ignore]
        public override async Task CompilerPackage() { }

        public override InterpreterConfiguration Configuration {
            get {
                return PythonPaths.Python35?.Configuration ??
                    PythonPaths.Python35_x64?.Configuration;
            }
        }

        public override IEnumerable<string> SkipFilesInFullStdLibTest {
            get {
                yield return @"\lib2to3\tests\data\";
                yield return @"\test\badsyntax_";
            }
        }
    }
}
