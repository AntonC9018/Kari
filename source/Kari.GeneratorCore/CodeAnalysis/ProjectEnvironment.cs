using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore.CodeAnalysis
{
    public class ProjectEnvironmentData
    {
        public readonly string Directory;
        public readonly string NamespaceName;
        public readonly string GeneratedNamespace;
        public readonly INamespaceSymbol RootNamespace;
        
        public MasterEnvironment Master => MasterEnvironment.SingletonInstance;
        public Compilation Compilation => Master.Compilation;
        public RelevantSymbols Symbols => Master.Symbols;

        public ProjectEnvironmentData(string directory, string namespaceName, INamespaceSymbol rootNamespace)
        {
            Directory = directory;
            NamespaceName = namespaceName;
            RootNamespace = rootNamespace;
            GeneratedNamespace = NamespaceName.Combine(Master.GeneratedNamespaceSuffix);
        }

        /// <summary>
        /// Writes the text to a file with the given file name, 
        /// placed in the directory of this project, with the current /Generated suffix appended to it.
        /// </summary>
        public void WriteLocalFile(string fileName, string text)
        {
            var outputPath = Path.Combine(Directory, Master.GeneratedDirectorySuffix, fileName);
            File.WriteAllText(outputPath, text);
        }
    }


    public class ProjectEnvironment : ProjectEnvironmentData
    {
        // Any registered resources, like small pieces of data common to the project
        public readonly Resources<object> Resources = new Resources<object>(5);

        // Cached symbols
        public readonly List<INamedTypeSymbol> Types = new List<INamedTypeSymbol>();
        public readonly List<INamedTypeSymbol> TypesWithAttributes = new List<INamedTypeSymbol>();
        public readonly List<IMethodSymbol> MethodsWithAttributes = new List<IMethodSymbol>();

        public ProjectEnvironment(string directory, string namespaceName, INamespaceSymbol rootNamespace) 
            : base(directory, namespaceName, rootNamespace)
        {
        }

        /// <summary>
        /// Asynchronously collects and caches relevant symbols.
        /// </summary>
        public Task Collect()
        {
            return Task.Run(() => {
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
            });
        }
    }
}
