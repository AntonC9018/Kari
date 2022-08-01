using System.Linq;
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
        private NamedLogger _logger;

        public void Initialize()
        {
            _logger = new NamedLogger("TerminalPlugin");

            TerminalProject = MasterEnvironment.Instance.AllProjectDatas.FirstOrDefault(
                project => project.Name == terminalProject);
            
            if (TerminalProject is null)
            {
                _logger.LogError($"Terminal project `{terminalProject}` could not be found");
                return;
            }

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
                    const string hint = "Terminal";

                    TerminalProject.AddCodeFragment(
                        CodeFragment.CreateFromBuilder(
                            "ParsersBasics.cs", hint, ParsersAnalyzer.TransformMaster()));

                    TerminalProject.AddCodeFragment(
                        CodeFragment.CreateFromBuilder(
                            "CommandBasics.cs", hint, CommandsAnalyzer.TransformBasics()));

                    MasterEnvironment.Instance.RootPseudoProject.AddCodeFragment(
                        CodeFragment.CreateFromBuilder(
                            "CommandsInitialization.cs", hint, 
                            CommandsAnalyzer.TransformBuiltin(
                                MasterEnvironment.Instance.RootPseudoProject, _commandAnalyzers)));

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