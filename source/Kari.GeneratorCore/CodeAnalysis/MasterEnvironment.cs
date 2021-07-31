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
        public static MasterEnvironment SingletonInstance { get; private set; }
        public string GeneratedDirectorySuffix { get; set; } = "Generated";
        public string GeneratedNamespaceSuffix => GeneratedDirectorySuffix;

        public readonly ProjectEnvironmentData RootPseudoProject;
        public INamespaceSymbol RootNamespace => RootPseudoProject.RootNamespace;
        public string ProjectRootDirectory => RootPseudoProject.Directory;

        public readonly CancellationToken CancellationToken;
        public readonly Compilation Compilation;
        public readonly RelevantSymbols Symbols;
        /// The very root namespace of the project.
        public readonly string RootNamespaceName;
        public readonly List<ProjectEnvironment> Projects = new List<ProjectEnvironment>();
        public readonly List<IAdministrator> Administrators = new List<IAdministrator>(5);

        /// <summary>
        /// Initializes the MasterEnvironment and replaces the global singleton instance.
        /// </summary>
        public MasterEnvironment(Compilation compilation, string rootNamespace, string rootDirectory, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            Compilation = compilation;
            Symbols = new RelevantSymbols(compilation);
            RootNamespaceName = rootNamespace;
            RootPseudoProject = new ProjectEnvironmentData(
                rootDirectory, rootNamespace, Compilation.GetNamespace(rootNamespace));
            SingletonInstance = this;
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
                    var environment = new ProjectEnvironment(projectDirectory, namespaceName, projectNamespace);
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
                var editorEnvironment = new ProjectEnvironment(editorDirectory, namespaceName + ".Editor", editorProjectNamespace);
                Projects.Add(editorEnvironment);
            }
        }

        public void InitializeAdministrators()
        {
            foreach (var admin in Administrators)
            {
                admin.Initialize();
            }
        }

        public Task Collect()
        {
            var cachingTasks = Projects.Select(project => project.Collect());
            var managerTasks = Administrators.Select(admin => admin.Collect());
            return Task.Factory.ContinueWhenAll(
                cachingTasks.ToArray(), (_) => Task.WhenAll(managerTasks), CancellationToken);
        }

        public void RunCallbacks()
        {
            var infos = new List<CallbackInfo>(); 
            foreach (var admin in Administrators)
            foreach (var callback in admin.GetCallbacks())
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
            var managerTasks = Administrators.Select(admin => admin.Generate());
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

    public interface IAdministrator
    {
        /// <summary>
        /// Method called after a reference to MasterEnvironment has been set.
        /// The MasterEnvironment already contains the projects at this point.
        /// </summary>
        void Initialize();

        /// <summary>
        /// The method called asynchronously by the MasterEnvironment to initiate the symbol collection process.
        /// You must initiate the symbol collecting processes of the resources you control.
        /// </summary>
        Task Collect();

        /// <summary>
        /// The method through which the MasterEnvironment figures out the dependencies between the different
        /// resources and administrators. This function must return the callbacks with the associated
        /// priority numbers. The lower the priority, the sooner the callback will execute.
        /// By knowing what priority number a different Administrator will use, you can run your handler
        /// right before or right after theirs, by setting the priority of your handler to a lower or to 
        /// a higher value respectively.
        /// </summary>
        IEnumerable<CallbackInfo> GetCallbacks();

        /// <summary>
        /// The method called asynchronously by the MasterEnvironment to initiate the code generation process.
        /// </summary>
        Task Generate();
    }
    
    public interface IAnalyzer
    {
        void Collect(ProjectEnvironment project);
    }

    public interface IAnalyzerMaster<T> : IAdministrator where T : IAnalyzer
    {
        T[] Slaves { get; set; }
        IGenerator<T> CreateGenerator();
        
    }

    public static class AnalyzerMasterHelpers
    {
        public static void Slaves_Initialize<T>(this IAnalyzerMaster<T> master) 
            where T : IAnalyzer, new()
        {
            var projects = MasterEnvironment.SingletonInstance.Projects;
            var slaves = new T[projects.Count]; 
            master.Slaves = slaves;
            for (int i = 0; i < projects.Count; i++)
            {
                slaves[i] = new T();
            }
        }

        public static void Slaves_Collect<T>(this IAnalyzerMaster<T> master) 
            where T : IAnalyzer
        {
            var projects = MasterEnvironment.SingletonInstance.Projects;
            var slaves = master.Slaves;
            for (int i = 0; i < slaves.Length; i++)
            {
                slaves[i].Collect(projects[i]);
            }
        }

        public static Task Slaves_CollectTask<T>(this IAnalyzerMaster<T> master) 
            where T : IAnalyzer
        {
            return Task.Run(() => Slaves_Collect(master));
        }

        public static void Slaves_Generate<T>(this IAnalyzerMaster<T> master, string fileName)
            where T : IAnalyzer
        {
            var projects = MasterEnvironment.SingletonInstance.Projects;
            var slaves = master.Slaves;
            var generator = master.CreateGenerator();
            for (int i = 0; i < slaves.Length; i++)
            {
                generator.m = slaves[i];
                generator.Project = projects[i];

                if (generator.ShouldWrite())
                {
                    projects[i].WriteLocalFile(fileName, generator.TransformText());
                }
            }
        } 

        public static Task Slaves_GenerateTask<T>(this IAnalyzerMaster<T> master, string fileName) 
            where T : IAnalyzer
        {
            return Task.Run(() => Slaves_Generate(master, fileName));
        }

        public static IEnumerable<(ProjectEnvironment, T)> ProjectSlave_Enumerate<T>(this IAnalyzerMaster<T> master) 
            where T : IAnalyzer
        {
            var projects = MasterEnvironment.SingletonInstance.Projects;
            var slaves = master.Slaves;
            for (int i = 0; i < slaves.Length; i++)
            {
                yield return (projects[i], slaves[i]);
            }
        }
    }
}