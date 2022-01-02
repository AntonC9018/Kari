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
            AdministratorHelpers.Initialize(ref _parserAnalyzers);
            AdministratorHelpers.Initialize(ref _commandAnalyzers);
            ParserDatabase.InitializeSingleton(new ParserDatabase(TerminalProject));
        }

        public Task Collect()
        {
            return Task.WhenAll(
                AdministratorHelpers.CollectAsync(_parserAnalyzers),
                AdministratorHelpers.CollectAsync(_commandAnalyzers))
            .ContinueWith(t => {
                for (int i = 0; i < _commandAnalyzers.Length; i++)
                {
                    _commandAnalyzers[i].InitializeParsers(); // callback functions
                }
            });
        }

        public Task Generate()
        {
            return Task.WhenAll(
                AdministratorHelpers.GenerateAsync(_parserAnalyzers, "Parsers.cs"),
                AdministratorHelpers.GenerateAsync(_commandAnalyzers, "Commands.cs"),
                Task.Run(delegate
                {
                    string hint = "Terminal";
                    TerminalProject.AddCodeFragment(new CodeFragment
                    {
                        FileNameHint = "ParsersBasics.cs",
                        NameHint = hint,
                        CodeBuilder = ParsersAnalyzer.TransformMaster()
                    });
                    TerminalProject.AddCodeFragment(new CodeFragment
                    {
                        FileNameHint = "CommandBasics.cs",
                        NameHint = hint,
                        CodeBuilder = CommandsAnalyzer.TransformBasics()
                    });
                    MasterEnvironment.Instance.RootPseudoProject.AddCodeFragment(new CodeFragment
                    {
                        FileNameHint = "CommandsInitialization.cs",
                        NameHint = hint,
                        CodeBuilder = CommandsAnalyzer.TransformBuiltin(
                            MasterEnvironment.Instance.RootPseudoProject, _commandAnalyzers),
                    });
                    AdministratorHelpers.AddCodeString(TerminalProject, "TerminalAnnotations.cs", hint, GetAnnotations());
                })
            );
        }

        public string GetAnnotations()
        {
            return DummyParserAnnotations.Text + DummyCommandAnnotations.Text;
        }
    }
}