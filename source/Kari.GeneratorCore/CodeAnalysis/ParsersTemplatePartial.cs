using System.Collections.Generic;
using Kari.GeneratorCore.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore
{
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
    

    public partial class ParsersTemplate
    {
        private string DefinitionsNamespace;
        private readonly List<CustomParserInfo> _customParserInfos;
        private readonly List<CustomParserInfo> _customParserFunctionInfos;

        public ParsersTemplate()
        {
            _customParserInfos         = new List<CustomParserInfo>();
            _customParserFunctionInfos = new List<CustomParserInfo>();
        }

        public void CollectInfo(ProjectEnvironment environment, ParsersAdministrator master)
        {
            Namespace = environment.GetGeneratedNamespace();
            var parsersFullyQualifiedClassName = Namespace.Combine("Parsers");

            var symbols = environment.Symbols;

            foreach (var type in environment.TypesWithAttributes)
            {
                if (type.TryGetAttribute(symbols.ParserAttribute, out var parserAttribute))
                {
                    var info = new CustomParserInfo(type, parserAttribute, parsersFullyQualifiedClassName);
                    _customParserInfos.Add(info);
                    master.AddParser(info);
                }
            }

            foreach (var method in environment.MethodsWithAttributes)
            {
                if (!method.IsStatic) continue;

                if (method.TryGetAttribute(symbols.ParserAttribute, out var parserAttribute))
                {
                    var info = new CustomParserInfo(method, parserAttribute, parsersFullyQualifiedClassName);
                    _customParserFunctionInfos.Add(info);
                    master.AddParser(info);
                }
            }
        }     
    }
}