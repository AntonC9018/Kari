using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kari.GeneratorCore.CodeAnalysis
{
    public class Environment
    {
        public Compilation Compilation { get; }
        public RelevantSymbols Symbols { get; }
        public INamespaceSymbol RootNamespace { get; }
        
        public Environment(Compilation compilation, string rootNamespace, Action<string> logger)
        {
            Compilation = compilation;
            Symbols = new RelevantSymbols(compilation, logger);
            RootNamespace = compilation.GetNamespace(rootNamespace);
        }
    }
}
