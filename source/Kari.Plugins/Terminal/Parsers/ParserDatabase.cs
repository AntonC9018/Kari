using System.Collections.Generic;
using Kari.GeneratorCore;
using Kari.GeneratorCore.Workflow;
using Microsoft.CodeAnalysis;

namespace Kari.Plugins.Terminal
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
        public readonly ISymbol Symbol;
        public readonly ParserAttribute Attribute;
        public readonly ITypeSymbol Type;
        public readonly string TypeName;
        public readonly string FullyQualifiedName;

        private CustomParserInfo(ISymbol symbol, ParserAttribute attribute, string parsersFullyQualifiedClassName)
        {
            Attribute = attribute;
            Attribute.Name ??= symbol.Name;
            FullName = parsersFullyQualifiedClassName.Combine(Attribute.Name);
            FullyQualifiedName = symbol.GetFullyQualifiedName();
            Symbol = symbol;
            Next = null;
        }

        public CustomParserInfo(IMethodSymbol symbol, ParserAttribute attribute, string parsersFullyQualifiedClassName) 
            : this((ISymbol) symbol, attribute, parsersFullyQualifiedClassName)
        {
            Type = symbol.Parameters[symbol.Parameters.Length - 1].Type;
            TypeName = Type.GetFullyQualifiedName();
        }

        public CustomParserInfo(INamedTypeSymbol symbol, ParserAttribute attribute, string parsersFullyQualifiedClassName)
            : this((ISymbol) symbol, attribute, parsersFullyQualifiedClassName)
        {
            Type = symbol.TypeArguments[symbol.TypeArguments.Length - 1];
            TypeName = Type.GetFullyQualifiedName();
        }

        public string Name => Attribute.Name;
        public string FullName { get; }
        public CustomParserInfo Next { get; set; }
    }

    internal class ParserDatabase : Singleton<ParserDatabase>
    {
        internal const int CheckPriority = 1;
        private readonly Dictionary<ITypeSymbol, BuiltinParser> _builtinParsers = new Dictionary<ITypeSymbol, BuiltinParser>();
        private readonly Dictionary<ITypeSymbol, CustomParserInfo> _customParsersTypeMap = new Dictionary<ITypeSymbol, CustomParserInfo>();
        private readonly Logger _logger = new Logger("Parsers");

        internal static string GetFullyQualifiedParsersClassNameForProject(ProjectEnvironmentData environment)
        {
            return environment.GeneratedNamespace.Combine("Parsers");
        }

        public ParserDatabase(ProjectEnvironmentData terminalProject)
        {
            var parsersFullyQualifiedClassName = GetFullyQualifiedParsersClassNameForProject(terminalProject);
            _builtinParsers.Add(Symbols.Int,     new BuiltinParser(parsersFullyQualifiedClassName, "Int")      );
            _builtinParsers.Add(Symbols.Short,   new BuiltinParser(parsersFullyQualifiedClassName, "Short")    );
            _builtinParsers.Add(Symbols.Long,    new BuiltinParser(parsersFullyQualifiedClassName, "Long")     );
            _builtinParsers.Add(Symbols.Sbyte,   new BuiltinParser(parsersFullyQualifiedClassName, "Sbyte")    );
            _builtinParsers.Add(Symbols.Ushort,  new BuiltinParser(parsersFullyQualifiedClassName, "UShort")   );
            _builtinParsers.Add(Symbols.Uint,    new BuiltinParser(parsersFullyQualifiedClassName, "UInt")     );
            _builtinParsers.Add(Symbols.Ulong,   new BuiltinParser(parsersFullyQualifiedClassName, "ULong")    );
            _builtinParsers.Add(Symbols.Byte,    new BuiltinParser(parsersFullyQualifiedClassName, "Byte")     );
            _builtinParsers.Add(Symbols.Float,   new BuiltinParser(parsersFullyQualifiedClassName, "Float")    );
            _builtinParsers.Add(Symbols.Double,  new BuiltinParser(parsersFullyQualifiedClassName, "Double")   );
            _builtinParsers.Add(Symbols.Decimal, new BuiltinParser(parsersFullyQualifiedClassName, "Decimal")  );
            _builtinParsers.Add(Symbols.Char,    new BuiltinParser(parsersFullyQualifiedClassName, "Char")     );
            _builtinParsers.Add(Symbols.Bool,    new BuiltinParser(parsersFullyQualifiedClassName, "Bool")     );
            _builtinParsers.Add(Symbols.String,  new BuiltinParser(parsersFullyQualifiedClassName, "String")   );
        }

        public void AddParser(CustomParserInfo info)
        {
            lock (_customParsersTypeMap)
            {
                if (_customParsersTypeMap.TryGetValue(info.Type, out var parser))
                {
                    if (parser.Name == info.Name)
                    {
                        _logger.LogError($"Parser {info.Name} has been redefined at {info.Symbol.GetLocationInfo()}.");
                    }
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
        }

        private string GetInfo(IArgumentInfo argument)
        {
            return $"for type {argument.Symbol.Type} at {argument.Symbol.GetLocationInfo()}.";
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
                            _logger.LogError($"No such parser {customParser} {GetInfo(argument)}");
                            return null;
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

            _logger.LogError($"Found no parsers {GetInfo(argument)}.");
            return null;
        }
    }
}