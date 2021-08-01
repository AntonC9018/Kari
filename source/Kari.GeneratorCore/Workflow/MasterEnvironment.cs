using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;

namespace Kari.GeneratorCore.Workflow
{
    public class Singleton<T> where T : Singleton<T>
    {
        public static T Instance { get; private set; }
        public Singleton()
        {
            if (!(Instance is null)) throw new System.Exception("Cannot initialize a singleton multiple times.");
            Instance = (T) this;
        }
    }

    public class MasterEnvironment : Singleton<MasterEnvironment>
    {
        public string GeneratedDirectorySuffix { get; set; } = "Generated";
        public string GeneratedNamespaceSuffix => GeneratedDirectorySuffix;

        public ProjectEnvironmentData RootPseudoProject { get; private set; }
        public INamespaceSymbol RootNamespace => RootPseudoProject.RootNamespace;
        public string ProjectRootDirectory => RootPseudoProject.Directory;
        public Compilation Compilation { get; private set; }
        public RelevantSymbols Symbols { get; private set; }

        public readonly CancellationToken CancellationToken;
        public readonly string RootNamespaceName;
        public readonly List<ProjectEnvironment> Projects = new List<ProjectEnvironment>();
        public readonly List<IAdministrator> Administrators = new List<IAdministrator>(5);

        /// <summary>
        /// Initializes the MasterEnvironment and replaces the global singleton instance.
        /// </summary>
        public MasterEnvironment(string rootNamespace, string rootDirectory, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            RootNamespaceName = rootNamespace;
        }

        public void InitializeCompilation(string rootDirectory, ref Compilation compilation)
        {
            compilation = compilation.AddSyntaxTrees(
                Administrators.Select(a => 
                    CSharpSyntaxTree.ParseText(a.GetAnnotations())));
            Symbols = new RelevantSymbols(compilation);
            RootPseudoProject = new ProjectEnvironmentData(
                rootDirectory, RootNamespaceName, Compilation.GetNamespace(RootNamespaceName));
            Compilation = compilation;
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
        /// Get the content of the file with annotations associated with the given administrator.
        /// This information will be used to update the existing compilation.
        /// </summary>
        string GetAnnotations();

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

    public static class AnalyzerMaster
    {
        public static void Initialize<T>(ref T[] slaves) where T : IAnalyzer, new()
        {
            var projects = MasterEnvironment.Instance.Projects;
            slaves = new T[projects.Count]; 
            for (int i = 0; i < projects.Count; i++)
            {
                slaves[i] = new T();
            }
        }

        public static void Collect<T>(T[] slaves) where T : IAnalyzer
        {
            var projects = MasterEnvironment.Instance.Projects;
            for (int i = 0; i < slaves.Length; i++)
            {
                slaves[i].Collect(projects[i]);
            }
        }

        public static Task CollectTask<T>(T[] slaves) where T : IAnalyzer
        {
            return Task.Run(() => Collect(slaves));
        }

        public static void Generate<T>(T[] slaves, IGenerator<T> generator, string fileName)
            where T : IAnalyzer
        {
            var projects = MasterEnvironment.Instance.Projects;
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

        public static Task GenerateTask<T>(T[] slaves, IGenerator<T> generator, string fileName) 
            where T : IAnalyzer
        {
            return Task.Run(() => Generate(slaves, generator, fileName));
        }
    }
}