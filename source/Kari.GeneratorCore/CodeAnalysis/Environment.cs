using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kari.GeneratorCore.CodeAnalysis
{
    public class Environment
    {
        public Compilation Compilation { get; set; }
        public RelevantSymbols Symbols { get; set; }
        public INamespaceSymbol RootNamespace { get; set; }
        
        public Environment(Compilation compilation, string rootNamespace, Action<string> logger)
        {
            Compilation = compilation;
            Symbols = new RelevantSymbols(compilation, logger);
            RootNamespace = compilation.GetNamespace(rootNamespace);
        }
    }
}
