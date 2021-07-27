using System.Collections.Generic;
using Kari.GeneratorCore.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore
{
    public class CustomParserInfo
    {
        public readonly ParserAttribute Attribute;
        public readonly INamedTypeSymbol Type;
        public readonly string TypeName;
        public readonly string FullyQualifiedName;

        public CustomParserInfo(IMethodSymbol symbol, ParserAttribute attribute)
        {
            Attribute = attribute;
            if (Attribute.Name == null) Attribute.Name = symbol.Name;
            TypeName = symbol.Parameters[symbol.Parameters.Length - 1].Type.GetFullyQualifiedName();
            FullyQualifiedName = symbol.GetFullyQualifiedName();
            Next = null;
        }

        public CustomParserInfo(INamedTypeSymbol symbol, ParserAttribute attribute)
        {
            Attribute = attribute;
            if (Attribute.Name == null) Attribute.Name = symbol.Name;
            TypeName = symbol.TypeArguments[symbol.TypeArguments.Length - 1].GetFullyQualifiedName();
            FullyQualifiedName = symbol.GetFullyQualifiedName();
            Next = null;
        }

        public string Name => Attribute.Name;
        public CustomParserInfo Next { get; set; }
    }
    

    public partial class ParsersTemplate
    {
        private readonly Dictionary<ITypeSymbol, string> _builtinParsers;
        private readonly Dictionary<ITypeSymbol, CustomParserInfo> _customParsersTypeMap;
        private readonly List<CustomParserInfo> _customParserInfos;
        private readonly List<CustomParserInfo> _customParserFunctionInfos;

        public ParsersTemplate()
        {
            _builtinParsers            = new Dictionary<ITypeSymbol, string>();
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
        }

        public void CollectInfo(Environment environment)
        {
            var symbols = environment.Symbols;

            _builtinParsers.Add(symbols.Int,     "Int"      );
            _builtinParsers.Add(symbols.Short,   "Short"    );
            _builtinParsers.Add(symbols.Long,    "Long"     );
            _builtinParsers.Add(symbols.Sbyte,   "Sbyte"    );
            _builtinParsers.Add(symbols.Ushort,  "Ushort"   );
            _builtinParsers.Add(symbols.Uint,    "Uint"     );
            _builtinParsers.Add(symbols.Ulong,   "Ulong"    );
            _builtinParsers.Add(symbols.Byte,    "Byte"     );
            _builtinParsers.Add(symbols.Float,   "Float"    );
            _builtinParsers.Add(symbols.Double,  "Double"   );
            _builtinParsers.Add(symbols.Decimal, "Decimal"  );
            _builtinParsers.Add(symbols.Char,    "Char"     );
            _builtinParsers.Add(symbols.Bool,    "Bool"     );

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

        public string GetParserName(IArgumentInfo argument)
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
                    return parser.Name;
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
                    return parser.Name;
                }
            }

            throw new System.Exception($"Found no converters for type {argument.Symbol.Type}");
        }       
    }
}