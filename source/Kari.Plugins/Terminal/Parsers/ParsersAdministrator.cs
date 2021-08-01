using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kari.GeneratorCore;
using Kari.GeneratorCore.Workflow;
using Kari.Plugins.Terminal;
using Microsoft.CodeAnalysis;

namespace Kari.Plugins.Terminal
{
    public class ParsersAdministrator : Singleton<ParsersAdministrator>, IAdministrator
    {
        public ParsersAnalyzer[] _slaves;

        public const int CheckPriority = 1;
        private readonly Dictionary<ITypeSymbol, BuiltinParser> _builtinParsers = new Dictionary<ITypeSymbol, BuiltinParser>();
        private readonly Dictionary<ITypeSymbol, CustomParserInfo> _customParsersTypeMap = new Dictionary<ITypeSymbol, CustomParserInfo>();

        public void Initialize()
        {
            TerminalData.Load();
            ParserSymbols.Initialize();
            AnalyzerMaster.Initialize(ref _slaves);
            var parsersFullyQualifiedClassName = TerminalData.GetFullyQualifiedParsersClassNameForDefaultProject();
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

        public Task Collect()
        {
            return AnalyzerMaster.CollectTask(_slaves);
        }

        public Task Generate()
        {
            var slavesTask = AnalyzerMaster.GenerateTask(_slaves, new ParsersTemplate(), "Parsers.cs");
            var ownTask = Task.Run(() => {
                var project = TerminalData.TerminalProject;
                var template = new ParsersMasterTemplate();
                project.WriteLocalFile("ParsersBasics.cs", template.TransformText());
            });

            return Task.WhenAll(slavesTask, ownTask);
        }

        public IEnumerable<CallbackInfo> GetCallbacks()
        {
            // TODO: Add the check callback
            yield break;
        }

        public string GetAnnotations() => DummyParsersAnnotations.Text;
    }
}