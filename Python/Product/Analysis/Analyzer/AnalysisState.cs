using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public class AnalysisState {
        public readonly PythonFileContext Context;
        public readonly WithVersion<ISourceDocument> Document;

        public readonly WithVersion<Tokenization> Tokenization;
        public readonly WithVersion<IReadOnlyCollection<ErrorResult>> TokenizationErrors;

        public readonly WithVersion<PythonAst> Tree;
        public readonly WithVersion<IReadOnlyCollection<ErrorResult>> ParseErrors;

        public readonly WithVersion<IReadOnlyDictionary<string, PythonMemberType>> MemberList;

        public readonly WithVersion<object> Analysis;

        internal AnalysisState(ISourceDocument document, PythonFileContext context) {
            Document = new WithVersion<ISourceDocument>(document);
            Tokenization = new WithVersion<Tokenization>();
            TokenizationErrors = new WithVersion<IReadOnlyCollection<ErrorResult>>();
            Tree = new WithVersion<PythonAst>();
            ParseErrors = new WithVersion<IReadOnlyCollection<ErrorResult>>();
            MemberList = new WithVersion<IReadOnlyDictionary<string, PythonMemberType>>();
            Analysis = new WithVersion<object>();

            Context = context;
        }
    }
}
