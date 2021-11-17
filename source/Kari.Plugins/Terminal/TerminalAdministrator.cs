using System.Threading.Tasks;
using Kari.GeneratorCore.Workflow;
using Kari.Utils;

namespace Kari.Plugins.Terminal
{
    public class TerminalAdministrator : IAdministrator
    {
        [Kari.Arguments.Option("Namespace of the Terminal project.")]
        string terminalProject = "Terminal";

        internal ParsersAnalyzer[] _parserAnalyzers;
        internal CommandsAnalyzer[] _commandAnalyzers;
        internal static ProjectEnvironmentData TerminalProject { get; private set; }
        private Logger _logger;

        public void Initialize()
        {
            _logger = new Logger("TerminalPlugin");

            TerminalProject = MasterEnvironment.Instance.Projects.Find(
                project => project.RootNamespace.Name == terminalProject);
            
            if (TerminalProject is null)
            {
                _logger.LogError($"Terminal project `{terminalProject}` could not be found");
                return;
            }

            ParserSymbols.Initialize(_logger);
            CommandSymbols.Initialize(_logger);
            AnalyzerMaster.Initialize(ref _parserAnalyzers);
            AnalyzerMaster.Initialize(ref _commandAnalyzers);
            ParserDatabase.InitializeSingleton(new ParserDatabase(TerminalProject));
        }

        public Task Collect()
        {
            return Task.WhenAll(
                AnalyzerMaster.CollectAsync(_parserAnalyzers),
                AnalyzerMaster.CollectAsync(_commandAnalyzers))
            .ContinueWith(t => {
                for (int i = 0; i < _commandAnalyzers.Length; i++)
                {
                    _commandAnalyzers[i].InitializeParsers(); // callback functions
                }
            });
        }

        public Task Generate()
        {
            var commandsInitializationTemplate = new CommandsInitializationTemplate 
            { 
                Project = MasterEnvironment.Instance.RootPseudoProject, 
                m = _commandAnalyzers 
            };

            return Task.WhenAll(
                AnalyzerMaster.GenerateAsync(_parserAnalyzers, "Parsers.cs", new ParsersTemplate()),
                AnalyzerMaster.GenerateAsync(_commandAnalyzers, "Commands.cs", new CommandsTemplate()),
                TerminalProject.WriteFileAsync("ParsersBasics.cs", new ParsersMasterTemplate()),
                TerminalProject.WriteFileAsync("CommandBasics.cs", new CommandsBasicsTemplate()),
                MasterEnvironment.Instance.RootPseudoProject.WriteFileAsync("CommandsInitialization.cs", commandsInitializationTemplate),
                TerminalProject.WriteFileAsync("TerminalAnnotations.cs", GetAnnotations())
            );
        }

        public string GetAnnotations()
        {
            return DummyParserAnnotations.Text + DummyCommandAnnotations.Text;
        }
    }
}