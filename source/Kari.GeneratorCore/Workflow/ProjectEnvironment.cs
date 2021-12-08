using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public readonly Logger Logger;
        public readonly IFileWriter FileWriter;

        /// <summary>
        /// Directory with the source files, including the source code and the project files.
        /// </summary>
        public readonly string Directory;

        /// <summary>
        /// The fully qualified namespace name, as it is in the source code.
        /// This has nothing to do with the generated namespace.
        /// </summary>
        public readonly string NamespaceName;
        
        /// <summary>
        /// The name of the namespace that the generated code will end up in.
        /// </summary>
        public readonly string GeneratedNamespaceName;

        /// <summary>
        /// Shorthand for the MasterEnvironment singleton instance.
        /// </summary>
        public MasterEnvironment Master => MasterEnvironment.Instance;

        internal ProjectEnvironmentData(string directory, string namespaceName, IFileWriter fileWriter, Logger logger)
        {
            Directory = directory;
            NamespaceName = namespaceName;
            GeneratedNamespaceName = NamespaceName.Combine(Master.GeneratedNamespaceSuffix);
            FileWriter = fileWriter;
            Logger = logger;
        }

        /// <summary>
        /// Writes the text to a file with the given file name, 
        /// placed in the directory of this project, with the current /Generated suffix appended to it.
        /// </summary>
        public void WriteFile(string fileName, string text)
        {
            if (text == null) return;
            FileWriter.WriteCodeFile(fileName, text);
        }

        /// <inheritdoc cref="WriteFile"/>
        public Task WriteFileAsync(string fileName, string text)
        {
            return Task.Run(() => WriteFile(fileName, text));
        }

        internal void ClearOutput()
        {
            Logger.Log($"Clearing the generated output.");
            FileWriter.DeleteOutput();
        }
    }

    /// <summary>
    /// Caches symbols for a project.
    /// </summary>
    public class ProjectEnvironment : ProjectEnvironmentData
    {
        // Any registered resources, like small pieces of data common to the project
        public readonly Resources<object> Resources = new Resources<object>(5);
        public readonly INamespaceSymbol RootNamespace;

        // Cached symbols
        public readonly List<INamedTypeSymbol> Types = new List<INamedTypeSymbol>();
        public readonly List<INamedTypeSymbol> TypesWithAttributes = new List<INamedTypeSymbol>();
        public readonly List<IMethodSymbol> MethodsWithAttributes = new List<IMethodSymbol>();

        internal ProjectEnvironment(string directory, string namespaceName, INamespaceSymbol rootNamespace, IFileWriter fileWriter) 
            : base(directory, namespaceName, fileWriter, new Logger(rootNamespace.Name))
        {
            RootNamespace = rootNamespace;
        }

        /// <summary>
        /// Asynchronously collects and caches relevant symbols.
        /// </summary>
        internal Task Collect()
        {
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
                        if (MasterEnvironment.Instance.IndependentNamespaces.Contains(nspace.Name))
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
