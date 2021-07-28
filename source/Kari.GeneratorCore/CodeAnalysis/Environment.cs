using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore.CodeAnalysis
{
    public class Environment
    {
        public readonly Compilation Compilation;
        public readonly RelevantSymbols Symbols;
        public readonly INamespaceSymbol RootNamespace;
        public readonly List<INamedTypeSymbol> Types;
        public readonly List<INamedTypeSymbol> TypesWithAttributes;
        public readonly List<IMethodSymbol> MethodsWithAttributes; 
        
        public Environment(Compilation compilation, string rootNamespace, Action<string> logger)
        {
            Compilation = compilation;
            Symbols = new RelevantSymbols(compilation, logger);
            RootNamespace = compilation.GetNamespace(rootNamespace);

            Types                   = new List<INamedTypeSymbol>();
            TypesWithAttributes     = new List<INamedTypeSymbol>();
            MethodsWithAttributes   = new List<IMethodSymbol>();
        }

        public void Collect()
        {
            foreach (var type in RootNamespace.GetNotNestedTypes())
            {
                Types.Add(type);
                
                if (type.GetAttributes().Length > 0)
                    TypesWithAttributes.Add(type);
                
                foreach (var method in type.GetMethods())
                {
                    if (method.GetAttributes().Length > 0)
                        MethodsWithAttributes.Add(method);
                }
            }
        }
    }
}
