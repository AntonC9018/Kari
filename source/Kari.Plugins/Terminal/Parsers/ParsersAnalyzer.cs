using System.Collections.Generic;
using Kari.GeneratorCore.Workflow;
using Microsoft.CodeAnalysis;

namespace Kari.Plugins.Terminal
{
    public partial class ParsersAnalyzer : IAnalyzer
    {
        public string DefinitionsNamespace => TerminalData.TerminalProject.GeneratedNamespace;
        public readonly List<CustomParserInfo> _customParserInfos;
        public readonly List<CustomParserInfo> _customParserFunctionInfos;

        public ParsersAnalyzer()
        {
            _customParserInfos         = new List<CustomParserInfo>();
            _customParserFunctionInfos = new List<CustomParserInfo>();
        }

        public void Collect(ProjectEnvironment environment)
        {
            string parsersFullyQualifiedClassName = TerminalData.GetFullyQualifiedParsersClassNameForProject(environment);

            var symbols = environment.Symbols;

            foreach (var type in environment.TypesWithAttributes)
            {
                if (type.TryGetAttribute(ParserSymbols.ParserAttribute, out var parserAttribute))
                {
                    var info = new CustomParserInfo(type, parserAttribute, parsersFullyQualifiedClassName);
                    _customParserInfos.Add(info);
                    ParsersAdministrator.Instance.AddParser(info);
                }
            }

            foreach (var method in environment.MethodsWithAttributes)
            {
                if (!method.IsStatic) continue;

                if (method.TryGetAttribute(ParserSymbols.ParserAttribute, out var parserAttribute))
                {
                    var info = new CustomParserInfo(method, parserAttribute, parsersFullyQualifiedClassName);
                    _customParserFunctionInfos.Add(info);
                    ParsersAdministrator.Instance.AddParser(info);
                }
            }
        }     
    }
    
    public interface IParserInfo
    {
        string Name { get; }
        string FullName { get; }
    }

    public class BuiltinParser : IParserInfo
    {
        public string Name { get; }
        public string FullName { get; }
        public BuiltinParser(string parsersFullyQualifiedClassName, string name)
        {
            Name = name;
            FullName = parsersFullyQualifiedClassName.Combine(name);
        }
    }

    public class CustomParserInfo : IParserInfo
    {
        public readonly ParserAttribute Attribute;
        public readonly ITypeSymbol Type;
        public readonly string TypeName;
        public readonly string FullyQualifiedName;

        private CustomParserInfo(ISymbol symbol, string parsersFullyQualifiedClassName, ParserAttribute attribute)
        {
            Attribute = attribute;
            Attribute.Name ??= symbol.Name;
            FullName = parsersFullyQualifiedClassName.Combine(Attribute.Name);
            FullyQualifiedName = symbol.GetFullyQualifiedName();
            Next = null;
        }

        public CustomParserInfo(IMethodSymbol symbol, ParserAttribute attribute, string namespaceName) 
            : this((ISymbol) symbol, namespaceName, attribute)
        {
            Type = symbol.Parameters[symbol.Parameters.Length - 1].Type;
            TypeName = Type.GetFullyQualifiedName();
        }

        public CustomParserInfo(INamedTypeSymbol symbol, ParserAttribute attribute, string namespaceName)
            : this((ISymbol) symbol, namespaceName, attribute)
        {
            Type = symbol.TypeArguments[symbol.TypeArguments.Length - 1];
            TypeName = Type.GetFullyQualifiedName();
        }

        public string Name => Attribute.Name;
        public string FullName { get; }
        public CustomParserInfo Next { get; set; }
    }
}