using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kari.Arguments;
using Kari.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;

namespace Kari.GeneratorCore.Workflow
{
    public readonly struct ProjectNamesInfo
    {
        public string CommonProjectNamespaceName { get; init; } // "Common"
        /// <summary>
        /// Relative path to the generated folder or file (.cs will be appended in this case).
        /// Applies to each of the writers.
        /// </summary>
        public string GeneratedPath { get; init; } // "Generated"
        public string GeneratedNamespaceSuffix { get; init; } // "Generated"
        public string RootNamespaceName { get; init; } // ""
        public string ProjectRootDirectory { get; init; } // ""
    }

    public class MasterEnvironment : Singleton<MasterEnvironment>
    {
        /// <summary>
        /// All writers for the subprojects are generated from this writer.
        /// </summary>
        public IFileWriter RootWriter { get; set; }

        /// <summary>
        /// Holds the agnostic code, without dependencies.
        /// You should output agnostic code into this project.
        /// </summary>
        public ProjectEnvironmentData CommonPseudoProject { get; private set; }
        
        /// <summary>
        /// Holds any "master" or "runner" code.
        /// This is the only project that can reference all other projects.
        /// Place functions that bring together all generated code here, 
        /// like registering classes in a central registry, giving them id's etc.
        /// </summary>
        public ProjectEnvironmentData RootPseudoProject { get; private set; }
        public INamespaceSymbol RootNamespace { get; private set; }
        
        /// <summary>
        /// All symbols must come from this central compilation.
        /// </summary>
        public Compilation Compilation { get; private set; }

        public readonly Logger Logger;
        public readonly CancellationToken CancellationToken;
        public readonly List<ProjectEnvironment> Projects = new List<ProjectEnvironment>();
        public readonly List<IAdministrator> Administrators = new List<IAdministrator>(5);

        /// <summary>
        /// Initializes the MasterEnvironment and replaces the global singleton instance.
        /// </summary>
        public MasterEnvironment(CancellationToken cancellationToken, Logger logger)
        {
            CancellationToken = cancellationToken;
            Logger = logger;
        }

        public void TakeCommandLineArguments(ArgumentParser parser)
        {
            foreach (var admin in Administrators)
            {
                var result = parser.FillObjectWithOptionValues(admin.GetArgumentObject());
                if (result.IsError)
                {
                    foreach (var err in result.Errors)
                    {
                        Logger.LogError(err);
                    }
                }
            }
        }

        public void LogHelpForEachAdministrator(ArgumentParser parser)
        {
            foreach (var admin in Administrators)
            {
                Logger.LogPlain($"\nShowing help for `{admin}`.");
                Logger.LogPlain(parser.GetHelpFor(admin.GetArgumentObject()));
            }
        }

        public void InitializeCompilation(ref Compilation compilation, string rootNamespaceName)
        {
            Compilation = compilation.AddSyntaxTrees(
                Administrators.Select(a => CSharpSyntaxTree.ParseText(a.GetAnnotations())));

            Symbols.Initialize(Compilation);
            Compilation = Compilation;
            RootNamespace = Compilation.TryGetNamespace(rootNamespaceName);

            if (RootNamespace is null)
                Logger.LogError($"No such namespace `{rootNamespaceName}`");
        }

        private void AddProject(in ProjectEnvironment project, string commonProjectNamespaceName)
        {
            Logger.Log($"Adding project `{project.NamespaceName}`");
            Projects.Add(project);
            if (project.NamespaceName == commonProjectNamespaceName)
            {
                Logger.Log($"Found the common project `{project.NamespaceName}`");
                CommonPseudoProject = project;
            }
        }

        public void FindProjects(in ProjectNamesInfo projectNamesInfo, bool treatEditorAsSubproject)
        {
            if (RootWriter is null) 
            {
                Logger.LogError("The file writer must have been set by now.");
                return;
            }

            Logger.Log($"Searching for asmdef's in {projectNamesInfo.ProjectRootDirectory}");

            // find asmdef's
            foreach (var asmdef in Directory.EnumerateFiles(projectNamesInfo.ProjectRootDirectory, "*.asmdef", SearchOption.AllDirectories))
            {
                Logger.Log($"Found an asmdef file at {asmdef}");
                
                var projectDirectory = Path.GetDirectoryName(asmdef);
                var fileName = Path.GetFileNameWithoutExtension(asmdef);

                // We in fact have a bunch more info here that we could use.
                var asmdefJson = JObject.Parse(File.ReadAllText(asmdef));

                string namespaceName;
                if (asmdefJson.TryGetValue("rootNamespace", out JToken nameToken))
                {
                    namespaceName = nameToken.Value<string>();
                    if (namespaceName is null)
                    {
                        Logger.LogError($"Not found the namespace name of the project at {asmdef}");
                        continue;
                    }
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
                    Logger.LogWarning($"The namespace {namespaceName} deduced from project at {asmdef} could not be found in the compilation.");
                    continue;
                }

                // Check if any script files exist in the root
                if (Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly).Any()
                    // Check if any folders exist besides the Editor folder
                    || Directory.EnumerateDirectories(projectDirectory).Any(path => !path.EndsWith("Editor")))
                {
                    var generatedPathForProject = Path.Combine(projectDirectory, projectNamesInfo.GeneratedPath);
                    var environment = new ProjectEnvironment(
                        directory:                  projectDirectory,
                        namespaceName:              namespaceName,
                        generatedNamespaceName:     namespaceName.Combine(projectNamesInfo.GeneratedNamespaceSuffix),
                        rootNamespace:              projectNamespace,
                        fileWriter:                 RootWriter.GetWriter(generatedPathForProject));
                    // TODO: Assume no duplicates for now, but this will have to be error-checked.
                    AddProject(environment, projectNamesInfo.CommonProjectNamespaceName);
                }

                // !!! 
                // Actually, it does not work like I supposed it works
                // You have to have a separate asmdef for all editor projects, which is fair, I guess.

                // Check if "Editor" is in the array of included platforms.
                // TODO: I'm not sure if not-editor-only projects need this string here.
                // if (!asmdefJson.TryGetValue("includePlatforms", out JToken platformsToken)
                //     || !platformsToken.Children().Any(token => token.Value<string>() == "Editor"))
                // {
                //     continue;
                // }

                // if (!treatEditorAsSubproject) 
                //     continue;
                // var editorProjectNamespace = projectNamespace.GetNamespaceMembers().FirstOrDefault(n => n.Name == "Editor");
                // if (editorProjectNamespace is null)
                //     continue;
                // var editorDirectory = Path.Combine(projectDirectory, "Editor");
                // if (!Directory.Exists(editorDirectory))
                // {
                //     Logger.LogWarning($"Found an editor project {namespaceName}, but no `Editor` folder.");
                //     continue;
                // }
                // var editorEnvironment = new ProjectEnvironment(
                //     directory:      editorDirectory,
                //     namespaceName:  namespaceName.Combine("Editor"),
                //     rootNamespace:  editorProjectNamespace,
                //     fileWriter:     RootWriter.GetWriter(Path.Combine(editorDirectory, GeneratedPath)));
                    
                // AddProject(editorEnvironment);
            }
        }

        public void InitializePseudoProjects(in ProjectNamesInfo projectNamesInfo)
        {
            var generatedNamespaceName = Path.Combine(projectNamesInfo.RootNamespaceName, projectNamesInfo.GeneratedNamespaceSuffix);
            if (Projects.Count == 0)
            {
                var rootProject = new ProjectEnvironment(
                    directory:              projectNamesInfo.ProjectRootDirectory,
                    namespaceName:          projectNamesInfo.RootNamespaceName,
                    generatedNamespaceName: generatedNamespaceName,
                    rootNamespace:          RootNamespace,
                    fileWriter:             RootWriter);
                AddProject(rootProject, projectNamesInfo.CommonProjectNamespaceName);
                RootPseudoProject = rootProject;
            }
            else
            {
                RootPseudoProject = new ProjectEnvironmentData(
                    directory:              projectNamesInfo.ProjectRootDirectory,
                    namespaceName:          projectNamesInfo.RootNamespaceName,
                    generatedNamespaceName: generatedNamespaceName,
                    fileWriter:             RootWriter,
                    logger:                 new Logger("Root")
                );
            }

            if (CommonPseudoProject is null) 
            {
                if (projectNamesInfo.CommonProjectNamespaceName is not null)
                {
                    Logger.LogWarning($"No common project `{projectNamesInfo.CommonProjectNamespaceName}`. The common files will be generated into root.");
                }

                CommonPseudoProject = RootPseudoProject;
            }
        }

        public void InitializeAdministrators()
        {
            foreach (var admin in Administrators)
            {
                admin.Initialize();
            }
        }

        public async Task Collect(HashSet<string> independentNamespaceNames)
        {
            var cachingTasks = Projects.Select(project => project.Collect(independentNamespaceNames));
            await Task.WhenAll(cachingTasks);
            CancellationToken.ThrowIfCancellationRequested();

            var managerTasks = Administrators.Select(admin => admin.Collect());
            await Task.WhenAll(managerTasks);
            CancellationToken.ThrowIfCancellationRequested();
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

        public void ClearOutput()
        {
            for (int i = 0; i < Projects.Count; i++)
            {
                Projects[i].ClearOutput();
            }
            RootPseudoProject.ClearOutput();
        }

        public void CloseWriters()
        {
            for (int i = 0; i < Projects.Count; i++)
            {
                Projects[i].FileWriter.Dispose();
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
}