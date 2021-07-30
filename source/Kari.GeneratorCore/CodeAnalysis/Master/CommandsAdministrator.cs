using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kari.GeneratorCore.CodeAnalysis;

namespace Kari.GeneratorCore
{
    public class TerminalData
    {
        public readonly ProjectEnvironment TerminalProject;

        private TerminalData(MasterEnvironment master)
        {
            // For now, let's just say we output to the project with "Terminal" in its name
            TerminalProject = master.Projects.Find(
                project => project.RootNamespace.Name.Contains("Terminal"));
        }

        public static TerminalData Creator(MasterEnvironment environment) => new TerminalData(environment);
    }

    /// Generates project-wide code for the essential commands
    /// Manages individual per-project CommandsTemplates
    public partial class CommandsAdministrator : AdministratorBase
    {
        private ParsersAdministrator _parsers;
        public const int InitializeParsersPriority = ParsersAdministrator.CheckPriority + 1;

        public override void Initialize()
        {
            // Cache references to other components
            _parsers = _masterEnvironment.Administrators.Get<ParsersAdministrator>();
            AddResourceToAllProjects<CommandsTemplate>();
            _masterEnvironment.LoadResource(TerminalData.Creator);
        }

        public override Task Collect()
        { 
            return WhenAllResources<CommandsTemplate>((project, commands) => commands.Collect(project));
        }

        public override Task Generate()
        {
            return Task.WhenAll(
                WriteFilesTask<CommandsTemplate>("Commands.cs"),
                Task.Run(() => {
                    var terminal = _masterEnvironment.Resources.Get<TerminalData>();

                    if (terminal.TerminalProject is null)
                    {
                        // Just write in the root
                        WriteOwnFile("CommandBasics.cs", "???");
                        return;
                    }

                    terminal.TerminalProject.WriteLocalFile("CommandBasics.cs", "???");
                })
            );
        }

        public override IEnumerable<CallbackInfo> GetCallbacks()
        {
            yield return new CallbackInfo(InitializeParsersPriority, InitializeParsersCallback);
        }

        private void InitializeParsersCallback()
        {
            foreach (var commands in GetResourceFromAllProjects<CommandsTemplate>())
            {
                commands.InitializeParsers(_parsers); // callback functions
            }
        }
    }
}