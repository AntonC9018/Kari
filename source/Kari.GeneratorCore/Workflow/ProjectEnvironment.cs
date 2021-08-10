using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore.Workflow
{
    public class ProjectEnvironmentData
    {
        public readonly Logger Logger;
        public readonly IFileWriter FileWriter;
        public readonly string Directory;
        public readonly string NamespaceName;
        public readonly string GeneratedNamespace;
        
        public MasterEnvironment Master => MasterEnvironment.Instance;

        public ProjectEnvironmentData(string directory, string namespaceName, IFileWriter fileWriter, Logger logger)
        {
            Directory = directory;
            NamespaceName = namespaceName;
            GeneratedNamespace = NamespaceName.Combine(Master.GeneratedNamespaceSuffix);
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

        public Task WriteFileAsync(string fileName, string text)
        {
            return Task.Run(() => WriteFile(fileName, text));
        }

        public Task WriteFileAsync(string fileName, ICodeTemplate template)
        {
            return Task.Run(() => WriteFile(fileName, template.TransformText()));
        }

        public void ClearOutput()
        {
            Logger.Log($"Clearing the generated output.");
            FileWriter.DeleteOutput();
        }
    }


    public class ProjectEnvironment : ProjectEnvironmentData
    {
        // Any registered resources, like small pieces of data common to the project
        public readonly Resources<object> Resources = new Resources<object>(5);
        public readonly INamespaceSymbol RootNamespace;

        // Cached symbols
        public readonly List<INamedTypeSymbol> Types = new List<INamedTypeSymbol>();
        public readonly List<INamedTypeSymbol> TypesWithAttributes = new List<INamedTypeSymbol>();
        public readonly List<IMethodSymbol> MethodsWithAttributes = new List<IMethodSymbol>();

        public ProjectEnvironment(string directory, string namespaceName, INamespaceSymbol rootNamespace, IFileWriter fileWriter) 
            : base(directory, namespaceName, fileWriter, new Logger(rootNamespace.Name))
        {
            RootNamespace = rootNamespace;
        }

        /// <summary>
        /// Asynchronously collects and caches relevant symbols.
        /// </summary>
        public Task Collect()
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
