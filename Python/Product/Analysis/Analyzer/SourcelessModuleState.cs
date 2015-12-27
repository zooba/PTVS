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
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    class SourcelessModuleState : IAnalysisState {
        private readonly PythonLanguageService _analyzer;
        private readonly ISourceDocument _document;
        private readonly LanguageFeatures _features;
        private readonly string _namePrefix;
        private IDirectAttributeProvider _module;

        public static SourcelessModuleState Create(
            PythonLanguageService analyzer,
            string name,
            Func<VariableKey,ISourceDocument, IDirectAttributeProvider> createModule
        ) {
            var state = new SourcelessModuleState(analyzer, name);
            state._module = createModule(new VariableKey(state, name), state.Document);
            return state;
        }

        private SourcelessModuleState(PythonLanguageService analyzer, string name) {
            _analyzer = analyzer;
            _features = new LanguageFeatures(_analyzer.Configuration.Version, FutureOptions.Invalid);

            var moniker = analyzer.Configuration.InterpreterPath + "$" + name;
            _document = new SourcelessDocument(moniker);
            _namePrefix = name + ".";
        }

        public PythonLanguageService Analyzer => _analyzer;
        public PythonFileContext Context => null;
        public ISourceDocument Document => _document;
        public LanguageFeatures Features => _features;
        public IDirectAttributeProvider Module => _module;
        public long Version => 1;

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

        public async Task AddTypesAsync(string name, IAnalysisSet values, CancellationToken cancellationToken) {
            //await ReportErrorAsync("read-only-attribute", "cannot assign to '" + name + "'", 
        }

        public async Task<IAnalysisSet> GetTypesAsync(string name, CancellationToken cancellationToken) {
            return await _module.GetAttribute(name, cancellationToken);
        }

        public Task<IReadOnlyCollection<string>> GetVariablesAsync(CancellationToken cancellationToken) {
            return _module.GetAttributeNames(cancellationToken);
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
