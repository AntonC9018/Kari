// MyPlugin file generated by Kari. Feel free to change it or remove this message.
using System.Collections.Generic;
using Kari.GeneratorCore.Workflow;
using Kari.Utils;
using Microsoft.CodeAnalysis;

namespace Kari.Plugins.MyPlugin
{
    // An analyzer will be created per project. 
    // It manages collecting specific information with a single project as input.
    public class MyPluginAnalyzer : ICollectSymbols, IGenerateCode
    {
        private readonly List<MyPluginInfo> _infos = new List<MyPluginInfo>();
        
        public void CollectSymbols(ProjectEnvironment environment)
        {
            foreach (var type in environment.TypesWithAttributes)
            {
                if (type.HasAttribute(MyPluginSymbols.MyPluginAttribute.symbol))
                {
                    _infos.Add(new MyPluginInfo(type));
                }
            }
        }
        
        public void GenerateCode(ProjectEnvironmentData project, ref CodeBuilder builder)
        {
            // Returing null implies no output should be generated for the given template.
            if (_infos.Count == 0) 
                return null;

            builder.AppendLine($"namespace {project.GeneratedNamespaceName}");
            builder.StartBlock();
    
            // IMPORTANT: 
            // Put the usings inside the namespace, because Kari may do single-file output
            // in which case the generated code might be messed up if you pull in any symbols.
            // Try to scope things as much as possible and fully-qualify names where appropriate.
            builder.AppendLine("using System;");
            builder.AppendLine("public static class MarkedTypes");
            builder.StartBlock();

            foreach (var info in _infos)
            {
                builder.AppendLine($"public const string {info.Symbol.Name} = \"{info.Symbol.Name}\";");
            }
            
            builder.EndBlock();
            builder.EndBlock();
        }
    }

    // Store information in such structs/classes
    public readonly struct MyPluginInfo
    {
        public readonly INamedTypeSymbol Symbol;
        public MyPluginInfo(INamedTypeSymbol symbol)
        {
            Symbol = symbol;
        }
    }
}
