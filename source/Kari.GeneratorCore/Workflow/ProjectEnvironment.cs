using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kari.Utils;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore.Workflow
{
    /// <summary>
    /// aka a pseudoproject. 
    /// Holds metadata about a project.
    /// </summary>
    public class ProjectEnvironmentData
    {
        public Logger Logger { get; init; }

        /// <summary>
        /// Directory with the source files, including the source code and the project files.
        /// </summary>
        public string Directory { get; init; }

        /// <summary>
        /// The fully qualified namespace name, as it is in the source code.
        /// This has nothing to do with the generated namespace.
        /// </summary>
        public string NamespaceName { get; init; }
        
        /// <summary>
        /// The name of the namespace that the generated code will end up in.
        /// </summary>
        public string GeneratedNamespaceName { get; init; }

        /// <summary>
        /// Shorthand for the MasterEnvironment singleton instance.
        /// </summary>
        public MasterEnvironment Master => MasterEnvironment.Instance;

        public readonly Dictionary<string, List<CodeFragment>> FileNameToCodeFragments = new();

        
        /// <summary>
        /// Writes the text to a file with the given file name, 
        /// placed in the directory of this project, with the current /Generated suffix appended to it.
        /// </summary>
        public void AppendFileContent(string fileName, CodeFragment fragment)
        {
            lock (FileNameToCodeFragments)
            {
                if (!FileNameToCodeFragments.TryGetValue(fileName, out var list))
                    list = new List<CodeFragment>();
                list.Add(fragment);
            }
        }
    }

    /// <summary>
    /// Caches symbols for a project.
    /// </summary>
    public class ProjectEnvironment : ProjectEnvironmentData
    {
        // Any registered resources, like small pieces of data common to the project
        public readonly Resources<object> Resources = new Resources<object>(5);
        public INamespaceSymbol RootNamespace { get; init; }

        // Cached symbols
        public readonly List<INamedTypeSymbol> Types = new List<INamedTypeSymbol>();
        public readonly List<INamedTypeSymbol> TypesWithAttributes = new List<INamedTypeSymbol>();
        public readonly List<IMethodSymbol> MethodsWithAttributes = new List<IMethodSymbol>();

        /// <summary>
        /// Asynchronously collects and caches relevant symbols.
        /// </summary>
        internal Task Collect(HashSet<string> independentNamespaceNames)
        {
            // THOUGHT: For monolithic projects, this effectively runs on 1 core.
            return Task.Run(() => {
                foreach (var symbol in RootNamespace.GetMembers())
                {
                    void AddType(INamedTypeSymbol type)
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

                    if (symbol is INamedTypeSymbol type)
                    {
                        AddType(type);
                    }
                    else if (symbol is INamespaceSymbol nspace)
                    {
                        if (independentNamespaceNames.Contains(nspace.Name))
                        {
                            continue;
                        }
                        foreach (var topType in nspace.GetNotNestedTypes())
                        {
                            AddType(topType);
                        }
                    }
                }

                Logger.Log($"Collected {Types.Count} types, {TypesWithAttributes.Count} annotated types, {MethodsWithAttributes.Count} annotated methods.");
            });
        }
    }
}
