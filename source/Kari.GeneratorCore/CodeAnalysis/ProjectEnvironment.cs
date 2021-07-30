using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore.CodeAnalysis
{
    public class ProjectEnvironment
    {
        public readonly string Directory;
        public readonly string NamespaceName;
        
        // Backreference to master
        public readonly MasterEnvironment Master;
        public Compilation Compilation => Master.Compilation;
        public RelevantSymbols Symbols => Master.Symbols;

        // Any registered resources, e.g. Collectors/Templates.
        public readonly Resources<object> Resources = new Resources<object>(5);

        // Cached symbols
        public readonly INamespaceSymbol RootNamespace;
        public readonly List<INamedTypeSymbol> Types = new List<INamedTypeSymbol>();
        public readonly List<INamedTypeSymbol> TypesWithAttributes = new List<INamedTypeSymbol>();
        public readonly List<IMethodSymbol> MethodsWithAttributes = new List<IMethodSymbol>(); 
        
        public ProjectEnvironment(MasterEnvironment master, INamespaceSymbol rootNamespace, string namespaceName, string rootDirectory)
        {
            Master = master;
            RootNamespace = rootNamespace;
            NamespaceName = namespaceName;
            Directory = rootDirectory;
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

        /// <summary>
        /// Writes the text to a file with the given file name, 
        /// placed in the directory of this project, with the current /Generated suffix appended to it.
        /// </summary>
        public void WriteLocalFile(string fileName, string text)
        {
            var outputPath = Path.Combine(Directory, Master.GeneratedDirectorySuffix, fileName);
            File.WriteAllText(outputPath, text);
        }

        public string GetGeneratedNamespace()
        {
            return NamespaceName.Combine(Master.GeneratedNamespaceSuffix);
        }

        public void WriteLocalWithTemplate(string fileName, ITemplate template)
        {
            if (template.ShouldWrite())
            {
                template.Namespace = GetGeneratedNamespace();
                WriteLocalFile(fileName, template.TransformText());
            }
        }

        public void WriteLocalWithTemplateResource<TemplateT>(string fileName) where TemplateT : ITemplate
        {
            WriteLocalWithTemplate(fileName, Resources.Get<TemplateT>());
        }
    }
}
