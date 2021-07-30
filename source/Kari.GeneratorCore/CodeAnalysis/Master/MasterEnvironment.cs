using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace Kari.GeneratorCore.CodeAnalysis
{
    public static class Workflow
    {
        /// The order is:
        ///     Create MasterEnvironment;
        ///     FindProjects()
        ///     Add Managers
        ///     InitializeManagers()
        ///     Run Collect()
        ///     Initialize Managers callbacks 
        ///     Sort the callbacks by priotity
        ///     Run the callbacks in order
        ///     Exit on any errors
        ///     Generate code via Managers.

        public static async Task Main(Compilation Compilation)
        {
            var tokenSource = new CancellationTokenSource();
            var master = new MasterEnvironment(Compilation, "SomeProject", "SomeFolder", tokenSource.Token);
            master.FindProjects();
            
            // master.Managers.Add(new CommandsMaster());
            // Or a reflection based solution.
            master.AddAllAdministrators();

            master.InitializeAdministrators();
            await master.Collect();
            master.RunCallbacks();
            await master.GenerateCode();
        }
    }


    public class MasterEnvironment
    {
        public string GeneratedDirectorySuffix { get; set; } = "Generated";
        public readonly string ProjectRootDirectory;
        public readonly CancellationToken CancellationToken;
        public readonly Compilation Compilation;
        public readonly RelevantSymbols Symbols;
        /// The very root namespace of the project.
        public readonly INamespaceSymbol RootNamespace;
        public readonly List<ProjectEnvironment> Projects = new List<ProjectEnvironment>();
        public readonly Resources<AdministratorBase> Administrators = new Resources<AdministratorBase>(5);
        public readonly Resources<object> Resources = new Resources<object>(5);


        public MasterEnvironment(Compilation compilation, string rootNamespace, string rootDirectory, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            Compilation = compilation;
            Symbols = new RelevantSymbols(compilation);
            RootNamespace = Compilation.GetNamespace(rootNamespace);
            ProjectRootDirectory = rootDirectory;
        }

        public void FindProjects()
        {
            // find asmdef's
            foreach (var asmdef in Directory.EnumerateFiles(ProjectRootDirectory, "*.asmdef", SearchOption.AllDirectories))
            {
                var projectDirectory = Path.GetDirectoryName(asmdef);
                var fileName = Path.GetFileNameWithoutExtension(asmdef);

                // We in fact have a bunch more info here that we could use.
                var asmdefJson = JObject.Parse(File.ReadAllText(asmdef));

                string namespaceName;
                if (asmdefJson.TryGetValue("name", out JToken nameToken))
                {
                    namespaceName = nameToken.Value<string>();
                    // TODO: Report bettter
                    Debug.Assert(!(namespaceName is null));
                }
                else
                {
                    // Assume such naming convention.
                    namespaceName = fileName;
                }

                // Even the editor project will have this namespace, because of the convention.
                INamespaceSymbol projectNamespace = Compilation.TryGetNamespace(namespaceName);
                
                if (projectNamespace is null)
                {
                    // TODO: Report this in a better way
                    System.Console.WriteLine($"The namespace {namespaceName} deduced from asmdef project {fileName} could not be found in the compilation.");
                    continue;
                }

                // Check if any script files exist in the root
                if (Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly).Any()
                    // Check if any folders exist besided Editor folder
                    || Directory.EnumerateDirectories(projectDirectory).Any(path => !path.EndsWith("Editor")))
                {
                    var environment = new ProjectEnvironment(this, projectNamespace, projectDirectory);
                    // TODO: Assume no duplicates for now, but this will have to be error-checked.
                    Projects.Add(environment);
                }

                // Check if "Editor" is in the array of included platforms.
                // TODO: I'm not sure if not-editor-only projects need this string here.
                if (!asmdefJson.TryGetValue("includePlatforms", out JToken platformsToken)
                    || !platformsToken.Children().Any(token => token.Value<string>() == "Editor"))
                {
                    continue;
                }

                // Also, add the editor project as a separate project.
                // We take the convention that the namespace would be the same as that of asmdef, but with and appended .Editor.
                // So any namespace within project A, like A.B, would have a corresponding editor namespace of A.Editor.B
                // rather than A.B.Editor. 

                var editorProjectNamespace = projectNamespace.GetNamespaceMembers().FirstOrDefault(n => n.Name == "Editor");
                if (editorProjectNamespace is null)
                    continue;
                var editorDirectory = Path.Combine(projectDirectory, "Editor");
                if (!Directory.Exists(editorDirectory))
                {
                    // TODO: better error handling
                    System.Console.WriteLine($"Found an editor project {namespaceName}, but no `Editor` folder.");
                    continue;
                }
                var editorEnvironment = new ProjectEnvironment(this, editorProjectNamespace, editorDirectory);
                Projects.Add(editorEnvironment);
            }
        }

        public void InitializeAdministrators()
        {
            foreach (var admin in Administrators.Items)
            {
                admin.Initialize(this);
            }
        }

        public Task Collect()
        {
            var cachingTasks = Projects.Select(project => project.Collect());
            var managerTasks = Administrators.Items.Select(manager => manager.Collect());
            return Task.Factory.ContinueWhenAll(
                cachingTasks.ToArray(), (_) => Task.WhenAll(managerTasks), CancellationToken);
        }

        public void RunCallbacks()
        {
            var infos = new List<CallbackInfo>(); 
            foreach (var manager in Administrators.Items)
            foreach (var callback in manager.GetCallbacks())
            {
                infos.Add(callback);
            }

            infos.Sort((a, b) => a.Priority - b.Priority);

            for (int i = 0; i < infos.Count; i++)
            {
                infos[i].Callback();
            }
        }

        public Task GenerateCode()
        {
            var managerTasks = Administrators.Items.Select(manager => manager.Generate());
            return Task.WhenAll(managerTasks);
        }
    }

    public readonly struct CallbackInfo
    {
        public readonly int Priority;
        public readonly System.Action Callback;

        public CallbackInfo(int priority, System.Action callback)
        {
            Priority = priority;
            Callback = callback;
        }
    }

    public abstract class AdministratorBase
    {
        protected MasterEnvironment _masterEnvironment;

        public void Initialize(MasterEnvironment masterEnvironment)
        {
            _masterEnvironment = masterEnvironment;
            Initialize();
        }
        public virtual void Initialize() {}
        public abstract Task Collect();
        public abstract Task Generate();
        public abstract IEnumerable<CallbackInfo> GetCallbacks();

        protected void AddResourceToAllProjects<TResource>() where TResource : new()
        {
            foreach (var project in _masterEnvironment.Projects)
            {
                project.Resources.Add<TResource>(new TResource());
            }
        }

        protected IEnumerable<TResource> GetResourceFromAllProjects<TResource>()
        {
            foreach (var project in _masterEnvironment.Projects)
            {
                yield return project.Resources.Get<TResource>();
            }
        }

        protected Task WhenAllResources<T>(System.Func<ProjectEnvironment, T, Task> mapper)
        {
            var tasks = _masterEnvironment.Projects.Select(
                project => mapper(project, project.Resources.Get<T>()));

            return Task.WhenAll(tasks);
        }

        protected Task WhenAllResources<TResource>(System.Action<ProjectEnvironment, TResource> action)
        {
            return Task.Run(() => {
                foreach (var project in _masterEnvironment.Projects)
                {
                    action(project, project.Resources.Get<TResource>());
                }
            });
        }

        protected void WriteToFileForProject(ProjectEnvironment project, string fileName, CodePrinterBase printer)
        {
            if (printer.ShouldWrite())
            {
                project.WriteLocalFile(fileName, printer.TransformText());
            }
        } 

        protected Task WriteFilesTask<TCodePrinter>(string fileName) where TCodePrinter : CodePrinterBase 
        {
            return Task.Run(() =>
            {
                foreach (var project in _masterEnvironment.Projects)
                {
                    var printer = project.Resources.Get<TCodePrinter>();
                    if (printer.ShouldWrite())
                    {
                        project.WriteLocalFile(fileName, printer.TransformText());
                    }
                }
            });
        }

        protected void WriteOwnFile(string fileName, string text)
        {
            var outputPath = Path.Combine(_masterEnvironment.ProjectRootDirectory, _masterEnvironment.GeneratedDirectorySuffix, fileName);
            File.WriteAllText(outputPath, text);
        }

        protected Task WriteOwnFileTask(string fileName, string text) 
        {
            return Task.Run(() => WriteOwnFile(fileName, text));
        }
    }
}