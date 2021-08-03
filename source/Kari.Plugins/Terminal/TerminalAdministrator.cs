using System.Threading.Tasks;
using Kari.GeneratorCore.Workflow;

namespace Kari.Plugins.Terminal
{
    public class TerminalAdministrator : IAdministrator
    {
        public ParsersAnalyzer[] _parserAnalyzers;
        public CommandsAnalyzer[] _commandAnalyzers;
        public static ProjectEnvironmentData TerminalProject { get; private set; }
        private Logger _logger;

        public void Initialize()
        {
            // For now, let's just say we output to the project with "Terminal" in its name
            TerminalProject = MasterEnvironment.Instance.Projects.Find(
                project => project.RootNamespace.Name.Contains("Terminal"));
            // We default to the root project otherwise
            TerminalProject ??= MasterEnvironment.Instance.RootPseudoProject;

            _logger = new Logger("TerminalPlugin");

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