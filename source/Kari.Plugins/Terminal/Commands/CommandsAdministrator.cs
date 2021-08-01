using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kari.GeneratorCore;
using Kari.GeneratorCore.Workflow;

namespace Kari.Plugins.Terminal
{
    /// Generates project-wide code for the essential commands
    /// Manages individual per-project CommandsTemplates
    public partial class CommandsAdministrator : Singleton<CommandsAdministrator>, IAdministrator
    {
        public const int InitializeParsersPriority = ParsersAdministrator.CheckPriority + 1;
        public CommandsAnalyzer[] _slaves;

        public void Initialize()
        {
            AnalyzerMaster.Initialize(ref _slaves);
            TerminalData.Load();
            CommandSymbols.Initialize();
        }

        public Task Collect()
        { 
            return AnalyzerMaster.CollectTask(_slaves);
        }

        public Task Generate()
        {
            var slavesTask = AnalyzerMaster.GenerateTask(_slaves, new CommandsTemplate(), "Commands.cs");
            var ownTask = Task.Run(() => {
                {
                    var project = TerminalData.TerminalProject;
                    var template = new CommandsBasicsTemplate();
                    project.WriteLocalFile("CommandBasics.cs", template.TransformText());
                }
                {
                    var project = MasterEnvironment.Instance.RootPseudoProject;
                    var template = new CommandsInitializationTemplate();
                    template.Project = project;
                    template.m = this;
                    project.WriteLocalFile("CommandsInitialization.cs", template.TransformText());
                }
            });
            return Task.WhenAll(slavesTask, ownTask);
        }

        public IEnumerable<CallbackInfo> GetCallbacks()
        {
            yield return new CallbackInfo(InitializeParsersPriority, InitializeParsersCallback);
        }

        private void InitializeParsersCallback()
        {
            for (int i = 0; i < _slaves.Length; i++)
            {
                _slaves[i].InitializeParsers(); // callback functions
            }
        }

        public string GetAnnotations() => DummyCommandsAnnotations.Text;
    }
}