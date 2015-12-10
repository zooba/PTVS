using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Analysis.Values;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    class BuiltinsModuleState : IAnalysisState {
        private readonly PythonLanguageService _analyzer;
        private readonly BuiltinsModule _builtinsModule;
        private readonly ISourceDocument _document;
        private readonly LanguageFeatures _features;
        private readonly string _namePrefix;

        public BuiltinsModuleState(PythonLanguageService analyzer) {
            _analyzer = analyzer;
            _features = new LanguageFeatures(_analyzer.Configuration.Version, FutureOptions.Invalid);

            var name = _features.BuiltinsName;
            var moniker = analyzer.Configuration.InterpreterPath + "$" + name;
            _document = new SourcelessDocument(moniker);
            _builtinsModule = new BuiltinsModule(new VariableKey(this, name), name, name, moniker);
            _namePrefix = name + ".";
        }

        public PythonFileContext Context => null;
        public ISourceDocument Document => _document;
        public LanguageFeatures Features => _features;
        public BuiltinsModule Module => _builtinsModule;
        public long Version => 0;

        public Task DumpAsync(TextWriter output, CancellationToken cancellationToken) {
            throw new NotImplementedException();
        }

        public Task<PythonAst> GetAstAsync(CancellationToken cancellationToken) {
            return Task.FromResult<PythonAst>(null);
        }

        public Task<string> GetFullNameAsync(string name, SourceLocation location, CancellationToken cancellationToken) {
            return Task.FromResult(_namePrefix + name);
        }

        public Task<Tokenization> GetTokenizationAsync(CancellationToken cancellationToken) {
            return Task.FromResult<Tokenization>(null);
        }

        public Task<IAnalysisSet> GetTypesAsync(string name, CancellationToken cancellationToken) {
            if (!name.StartsWith(_namePrefix)) {
                return null;
            }
            return _builtinsModule.GetAttribute(name.Substring(_namePrefix.Length), cancellationToken);
        }

        public Task<IReadOnlyCollection<string>> GetVariablesAsync(CancellationToken cancellationToken) {
            return _builtinsModule.GetAttributeNames(cancellationToken);
        }

        public Task<bool> ReportErrorAsync(string code, string text, SourceLocation location, CancellationToken cancellationToken) {
            Debug.Fail("should never report errors in builtin module");
            return Task.FromResult(false);
        }

        public Tokenization TryGetTokenization() {
            return null;
        }

        public Task WaitForUpToDateAsync(CancellationToken cancellationToken) {
            return Task.FromResult<object>(null);
        }
    }
}
