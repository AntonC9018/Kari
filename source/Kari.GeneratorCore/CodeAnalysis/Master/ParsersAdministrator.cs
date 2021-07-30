using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kari.GeneratorCore.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore
{
    public class ParsersAdministrator : AdministratorBase
    {
        public const int CheckPriority = 1;
        private readonly Dictionary<ITypeSymbol, BuiltinParser> _builtinParsers = new Dictionary<ITypeSymbol, BuiltinParser>();
        private readonly Dictionary<ITypeSymbol, CustomParserInfo> _customParsersTypeMap = new Dictionary<ITypeSymbol, CustomParserInfo>();

        public override void Initialize()
        {
            AddResourceToAllProjects<ParsersTemplate>();
            _masterEnvironment.LoadResource(TerminalData.Creator);
            
            var symbols = _masterEnvironment.Symbols;
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
        }

        public void AddParser(CustomParserInfo info)
        {
            // Kinda meh, since it sort of kills the parallelism.
            // But it's fine, since we still have to iterate through all the types with attributes
            // and the custom parsers are relatively sparse anyway.
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

            throw new System.Exception($"Found no parsers for type {argument.Symbol.Type}");
        }  

        public override Task Collect()
        {
            return WhenAllResources<ParsersTemplate>((project, parsers) => parsers.CollectInfo(project, this));
        }

        public override Task Generate()
        {
            var collectiveTask = WriteFilesTask<ParsersTemplate>("Parsers.cs");
            var ownTask = Task.Run(() => {
                var terminal = _masterEnvironment.Resources.Get<TerminalData>();

                if (terminal.TerminalProject is null)
                {
                    // Just write in the root
                    WriteOwnFile("ParserBasics.cs", "???");
                    return;
                }

                terminal.TerminalProject.WriteLocalFile("ParserBasics.cs", "???");
            });
            return Task.WhenAll(collectiveTask, ownTask);
        }

        public override IEnumerable<CallbackInfo> GetCallbacks()
        {
            // TODO: Add the check callback
            yield break;
        }
    }
}