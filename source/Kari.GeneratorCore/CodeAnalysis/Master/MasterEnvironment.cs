using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace Kari.GeneratorCore.CodeAnalysis
{
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

    public abstract class MasterManagerBase
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

        protected void AddResourceToAllProjects<T>() where T : new()
        {
            foreach (var project in _masterEnvironment.Projects)
            {
                project.Resources.Add<T>(new T());
            }
        }

        protected IEnumerable<T> GetResourceFromAllProjects<T>()
        {
            foreach (var project in _masterEnvironment.Projects)
            {
                yield return project.Resources.Get<T>();
            }
        }
    }

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
            var master = new MasterEnvironment(Compilation, "SomeProject", tokenSource.Token);
            master.FindProjects("SomeFolder");
            master.Managers.Add(new CommandsMaster());
            master.InitializeManagers();
            await master.Collect();
            master.RunCallbacks();
            await master.GenerateCode();
        }
    }


    public class MasterEnvironment
    {
        public readonly CancellationToken CancellationToken;
        public readonly Compilation Compilation;
        public readonly RelevantSymbols Symbols;
        /// The very root namespace of the project.
        public readonly INamespaceSymbol RootNamespace;
        public readonly List<ProjectEnvironment> Projects = new List<ProjectEnvironment>();
        public readonly Resources<MasterManagerBase> Managers = new Resources<MasterManagerBase>(5);


        public MasterEnvironment(Compilation compilation, string rootNamespace, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            Compilation = compilation;
            Symbols = new RelevantSymbols(compilation);
            RootNamespace = Compilation.GetNamespace(rootNamespace);
        }

        public void FindProjects(string projectRootDirectory)
        {
            // find asmdef's
            foreach (var asmdef in Directory.EnumerateFiles(projectRootDirectory, "*.asmdef", SearchOption.AllDirectories))
            {
                var projectDirectory = Path.GetDirectoryName(asmdef);
                var fileName = Path.GetFileNameWithoutExtension(asmdef);

                // We in fact have a bunch more info here that we could use.
                var asmdefJson = JObject.Parse(File.ReadAllText(asmdef));

                string namespaceName;
                if (asmdefJson.TryGetValue("name", out JToken nameToken))
                {
                    namespaceName = ((string) nameToken);
                }
                else
                {
                    // Assume such naming convention.
                    namespaceName = fileName;
                }

                INamespaceSymbol projectNamespace;
                try
                {
                    projectNamespace = Compilation.GetNamespace(namespaceName);
                }
                catch
                {
                    // TODO: Report this in a better way
                    System.Console.WriteLine($"The namespace {namespaceName} deduced from asmdef project {fileName} could not be found in the compilation.");
                    continue;
                }

                var environment = new ProjectEnvironment(this, projectNamespace, projectDirectory);

                // TODO: Assume no duplicates for now, but this will have to be error-checked.
                Projects.Add(environment);
            }
        }

        public void InitializeManagers()
        {
            foreach (var manager in Managers.Items)
            {
                manager.Initialize(this);
            }
        }

        public Task Collect()
        {
            var cachingTasks = Projects.Select(project => project.Collect());
            var managerTasks = Managers.Items.Select(manager => manager.Collect());
            return Task.Factory.ContinueWhenAll(
                cachingTasks.ToArray(), (_) => Task.WhenAll(managerTasks), CancellationToken);
        }

        public void RunCallbacks()
        {
            var infos = new List<CallbackInfo>(); 
            foreach (var manager in Managers.Items)
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
            var managerTasks = Managers.Items.Select(manager => manager.Generate());
            return Task.WhenAll(managerTasks);
        }
    }
}