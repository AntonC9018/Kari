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
            var symbols = MasterEnvironment.Instance.Symbols;
            _builtinParsers.Add(symbols.Int,     new BuiltinParser(parsersFullyQualifiedClassName, "Int")      );
            _builtinParsers.Add(symbols.Short,   new BuiltinParser(parsersFullyQualifiedClassName, "Short")    );
            _builtinParsers.Add(symbols.Long,    new BuiltinParser(parsersFullyQualifiedClassName, "Long")     );
            _builtinParsers.Add(symbols.Sbyte,   new BuiltinParser(parsersFullyQualifiedClassName, "Sbyte")    );
            _builtinParsers.Add(symbols.Ushort,  new BuiltinParser(parsersFullyQualifiedClassName, "Ushort")   );
            _builtinParsers.Add(symbols.Uint,    new BuiltinParser(parsersFullyQualifiedClassName, "Uint")     );
            _builtinParsers.Add(symbols.Ulong,   new BuiltinParser(parsersFullyQualifiedClassName, "Ulong")    );
            _builtinParsers.Add(symbols.Byte,    new BuiltinParser(parsersFullyQualifiedClassName, "Byte")     );
            _builtinParsers.Add(symbols.Float,   new BuiltinParser(parsersFullyQualifiedClassName, "Float")    );
            _builtinParsers.Add(symbols.Double,  new BuiltinParser(parsersFullyQualifiedClassName, "Double")   );
            _builtinParsers.Add(symbols.Decimal, new BuiltinParser(parsersFullyQualifiedClassName, "Decimal")  );
            _builtinParsers.Add(symbols.Char,    new BuiltinParser(parsersFullyQualifiedClassName, "Char")     );
            _builtinParsers.Add(symbols.Bool,    new BuiltinParser(parsersFullyQualifiedClassName, "Bool")     );
            _builtinParsers.Add(symbols.String,  new BuiltinParser(parsersFullyQualifiedClassName, "String")   );
        }

        public void AddParser(CustomParserInfo info)
        {
            lock (_customParsersTypeMap)
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
                            _logger.LogError($"No such parser {parser.Name} for type {argument.Symbol.Type}");
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

            _logger.LogError($"Found no parsers for type {argument.Symbol.Type}");
            return null;
        }
    }
}