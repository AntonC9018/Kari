using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kari.GeneratorCore;
using Kari.GeneratorCore.CodeAnalysis;

namespace Kari.Plugins.Terminal
{
    /// Generates project-wide code for the essential commands
    /// Manages individual per-project CommandsTemplates
    public partial class CommandsAdministrator : IAdministrator, IAnalyzerMaster<CommandsAnalyzer>
    {
        public const int InitializeParsersPriority = ParsersAdministrator.CheckPriority + 1;
        public CommandsAnalyzer[] Slaves { get; set; }
        public IGenerator<CommandsAnalyzer> CreateGenerator() => new CommandsTemplate();

        public void Initialize()
        {
            this.Slaves_Initialize();
            TerminalData.Load();
            CommandSymbols.Initialize();
        }

        public Task Collect()
        { 
            return this.Slaves_CollectTask();
        }

        public Task Generate()
        {
            var slavesTask = this.Slaves_GenerateTask("Commands.cs");
            var ownTask = Task.Run(() => {
                {
                    var project = TerminalData.TerminalProject;
                    var template = new CommandsBasicsTemplate();
                    project.WriteLocalFile("CommandBasics.cs", template.TransformText());
                }
                {
                    var project = MasterEnvironment.SingletonInstance.RootPseudoProject;
                    var template = new CommandsInitializationTemplate();
                    template.Project = project;
                    template.m = this;
                    project.WriteLocalFile("CommandsInitialization.cs", template.TrasformText());
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
            for (int i = 0; i < Slaves.Length; i++)
            {
                Slaves[i].InitializeParsers(); // callback functions
            }
        }
    }
}