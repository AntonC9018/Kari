using System;
using System.Threading.Tasks;
using Kari.GeneratorCore.Workflow;

namespace Kari.Plugins.Terminal
{

    public static class TerminalData
    {
        private static bool _isInited = false;
        public static ProjectEnvironmentData TerminalProject { get; private set; }

        public static string GetFullyQualifiedParsersClassNameForProject(ProjectEnvironmentData environment)
        {
            return environment.GeneratedNamespace.Combine("Parsers");
        }

        public static string GetFullyQualifiedParsersClassNameForDefaultProject()
        {
            return TerminalProject.GeneratedNamespace.Combine("Parsers");
        }
        
        public static void Load()
        {
            if (_isInited) return;
            else _isInited = true;

            // For now, let's just say we output to the project with "Terminal" in its name
            TerminalProject = MasterEnvironment.Instance.Projects.Find(
                project => project.RootNamespace.Name.Contains("Terminal"));
            // We default to the root project otherwise
            TerminalProject ??= MasterEnvironment.Instance.RootPseudoProject;
        }
    }
}