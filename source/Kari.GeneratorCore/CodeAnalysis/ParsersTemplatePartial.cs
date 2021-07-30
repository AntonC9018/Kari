using System.Collections.Generic;
using Kari.GeneratorCore.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore
{
    public interface IParserInfo
    {
        string Name { get; }
    }

    public class BuiltinParser : IParserInfo
    {
        public string Name { get; }
        public BuiltinParser(string name) => Name = name;
    }

    public class CustomParserInfo : IParserInfo
    {
        public readonly ParserAttribute Attribute;
        public readonly ITypeSymbol Type;
        public readonly string TypeName;
        public readonly string FullyQualifiedName;

        public CustomParserInfo(IMethodSymbol symbol, ParserAttribute attribute)
        {
            Attribute = attribute;
            if (Attribute.Name == null) Attribute.Name = symbol.Name;
            Type = symbol.Parameters[symbol.Parameters.Length - 1].Type;
            TypeName = Type.GetFullyQualifiedName();
            FullyQualifiedName = symbol.GetFullyQualifiedName();
            Next = null;
        }

        public CustomParserInfo(INamedTypeSymbol symbol, ParserAttribute attribute)
        {
            Attribute = attribute;
            if (Attribute.Name == null) Attribute.Name = symbol.Name;
            Type = symbol.TypeArguments[symbol.TypeArguments.Length - 1];
            TypeName = Type.GetFullyQualifiedName();
            FullyQualifiedName = symbol.GetFullyQualifiedName();
            Next = null;
        }

        public string Name => Attribute.Name;
        public CustomParserInfo Next { get; set; }
    }
    

    public partial class ParsersTemplate
    {
        private readonly List<CustomParserInfo> _customParserInfos;
        private readonly List<CustomParserInfo> _customParserFunctionInfos;

        public ParsersTemplate()
        {
            _customParserInfos         = new List<CustomParserInfo>();
            _customParserFunctionInfos = new List<CustomParserInfo>();
        }

        public void CollectInfo(ProjectEnvironment environment, ParsersMaster master)
        {
            var symbols = environment.Symbols;

            foreach (var type in environment.TypesWithAttributes)
            {
                if (type.TryGetAttribute(symbols.ParserAttribute, out var parserAttribute))
                {
                    var info = new CustomParserInfo(type, parserAttribute);
                    _customParserInfos.Add(info);
                    master.AddParser(info);
                }
            }

            foreach (var method in environment.MethodsWithAttributes)
            {
                if (!method.IsStatic) continue;

                if (method.TryGetAttribute(symbols.ParserAttribute, out var parserAttribute))
                {
                    var info = new CustomParserInfo(method, parserAttribute);
                    _customParserFunctionInfos.Add(info);
                    master.AddParser(info);
                }
            }
        }     
    }
}