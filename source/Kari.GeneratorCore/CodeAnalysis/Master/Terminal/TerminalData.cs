using System.Threading.Tasks;
using Kari.GeneratorCore.CodeAnalysis;

namespace Kari.GeneratorCore
{
    public class TerminalData
    {
        public readonly ProjectEnvironment TerminalProject;
        public static TerminalData Creator(MasterEnvironment environment) => new TerminalData(environment);

        private TerminalData(MasterEnvironment master)
        {
            // For now, let's just say we output to the project with "Terminal" in its name
            TerminalProject = master.Projects.Find(
                project => project.RootNamespace.Name.Contains("Terminal"));
        }

        public static Task WriteLocalToProjectElseToRootHelper<TemplateT>(AdministratorBase admin, string fileName, System.Func<TemplateT> Creator) where TemplateT : ITemplate, new()
        {
            return Task.Run(() => {
                var terminal = admin._masterEnvironment.Resources.Get<TerminalData>();
                var template = Creator();

                if (terminal.TerminalProject is null)
                {
                    // Just write in the root
                    template.Namespace = admin._masterEnvironment.GeneratedRootNamespaceName;
                    admin.WriteOwnFile(fileName, template.TransformText());
                    return;
                }

                terminal.TerminalProject.WriteLocalWithTemplate(fileName, template);
            });
        }
    }
}