using System.Collections.Generic;
using Kari.GeneratorCore.Workflow;

namespace Kari.Plugins.Terminal
{
    public partial class ParsersAnalyzer : IAnalyzer
    {
        public string DefinitionsNamespace => TerminalAdministrator.TerminalProject.GeneratedNamespace;
        public readonly List<CustomParserInfo> _customParserInfos = new List<CustomParserInfo>();
        public readonly List<CustomParserInfo> _customParserFunctionInfos = new List<CustomParserInfo>();

        public void Collect(ProjectEnvironment environment)
        {
            string parsersFullyQualifiedClassName = ParserDatabase.GetFullyQualifiedParsersClassNameForProject(environment);

            foreach (var type in environment.TypesWithAttributes)
            {
                if (type.TryGetAttribute(ParserSymbols.ParserAttribute, environment.Logger, out var parserAttribute))
                {
                    var info = new CustomParserInfo(type, parserAttribute, parsersFullyQualifiedClassName);
                    _customParserInfos.Add(info);
                    ParserDatabase.Instance.AddParser(info);
                }
            }

            foreach (var method in environment.MethodsWithAttributes)
            {
                if (!method.IsStatic) continue;

                if (method.TryGetAttribute(ParserSymbols.ParserAttribute, environment.Logger, out var parserAttribute))
                {
                    var info = new CustomParserInfo(method, parserAttribute, parsersFullyQualifiedClassName);
                    _customParserFunctionInfos.Add(info);
                    ParserDatabase.Instance.AddParser(info);
                }
            }
        }     
    }
    
}