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
        private readonly Dictionary<ITypeSymbol, BuiltinParser> _builtinParsers;
        private readonly Dictionary<ITypeSymbol, CustomParserInfo> _customParsersTypeMap;
        private readonly List<CustomParserInfo> _customParserInfos;
        private readonly List<CustomParserInfo> _customParserFunctionInfos;

        public ParsersTemplate()
        {
            _builtinParsers            = new Dictionary<ITypeSymbol, BuiltinParser>();
            _customParsersTypeMap      = new Dictionary<ITypeSymbol, CustomParserInfo>();
            _customParserInfos         = new List<CustomParserInfo>();
            _customParserFunctionInfos = new List<CustomParserInfo>();
        }

        private void AddParser(CustomParserInfo info)
        {
            if (_customParsersTypeMap.TryGetValue(info.Type, out var parser))
            {
                while (parser.Next != null)
                {
                    parser = parser.Next;
                }
                parser.Next = info;
            }
            else
            {
                _customParsersTypeMap[info.Type] = info;
            }
        }

        public void CollectInfo(Environment environment)
        {
            var symbols = environment.Symbols;

            _builtinParsers.Add(symbols.Int,     new BuiltinParser("Int")      );
            _builtinParsers.Add(symbols.Short,   new BuiltinParser("Short")    );
            _builtinParsers.Add(symbols.Long,    new BuiltinParser("Long")     );
            _builtinParsers.Add(symbols.Sbyte,   new BuiltinParser("Sbyte")    );
            _builtinParsers.Add(symbols.Ushort,  new BuiltinParser("Ushort")   );
            _builtinParsers.Add(symbols.Uint,    new BuiltinParser("Uint")     );
            _builtinParsers.Add(symbols.Ulong,   new BuiltinParser("Ulong")    );
            _builtinParsers.Add(symbols.Byte,    new BuiltinParser("Byte")     );
            _builtinParsers.Add(symbols.Float,   new BuiltinParser("Float")    );
            _builtinParsers.Add(symbols.Double,  new BuiltinParser("Double")   );
            _builtinParsers.Add(symbols.Decimal, new BuiltinParser("Decimal")  );
            _builtinParsers.Add(symbols.Char,    new BuiltinParser("Char")     );
            _builtinParsers.Add(symbols.Bool,    new BuiltinParser("Bool")     );
            _builtinParsers.Add(symbols.String,  new BuiltinParser("String")   );

            foreach (var type in environment.TypesWithAttributes)
            {
                if (type.TryGetAttribute(symbols.ParserAttribute, out var parserAttribute))
                {
                    var info = new CustomParserInfo(type, parserAttribute);
                    _customParserInfos.Add(info);
                    AddParser(info);
                }
            }

            foreach (var method in environment.MethodsWithAttributes)
            {
                if (!method.IsStatic) continue;

                if (method.TryGetAttribute(symbols.ParserAttribute, out var parserAttribute))
                {
                    var info = new CustomParserInfo(method, parserAttribute);
                    _customParserFunctionInfos.Add(info);
                    AddParser(info);
                }
            }
        }

        public IParserInfo GetParser(IArgumentInfo argument)
        {
            var customParser = argument.GetAttribute().Parser;

            if (!(customParser is null))
            {
                if (_customParsersTypeMap.TryGetValue(argument.Symbol.Type, out var parser))
                {
                    while (parser.Name != customParser)
                    {
                        if (parser.Next is null)
                        {
                            throw new System.Exception($"No such parser {parser.Name} for type {argument.Symbol.Type}");
                        }
                        parser = parser.Next;
                    }
                    return parser;
                }
            }
            else 
            {
                if (_builtinParsers.TryGetValue(argument.Symbol.Type, out var result))
                {
                    return result;
                }

                if (_customParsersTypeMap.TryGetValue(argument.Symbol.Type, out var parser))
                {
                    return parser;
                }
            }

            throw new System.Exception($"Found no converters for type {argument.Symbol.Type}");
        }       
    }
}