using System;
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
using Microsoft.Win32.SafeHandles;
using static System.Diagnostics.Debug;
using System.Runtime.InteropServices;
using System.Buffers;

namespace Kari.GeneratorCore.Workflow;

public readonly struct ProjectNamesInfo
{
    public string CommonProjectNamespaceName { get; init; } = "Common";
    public string GeneratedNamespaceSuffix { get; init; } = "Generated";
    public string RootNamespaceName { get; init; } = "";
    public string ProjectRootDirectory { get; init; } = "";
}

public class MasterEnvironment : Singleton<MasterEnvironment>
{
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
            Logger.LogError($"No such root namespace `{rootNamespaceName}`");
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
        // Assert(RootWriter is not null, "The file writer must have been set by now.");

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
                var environment = new ProjectEnvironment
                {
                    Directory               = projectDirectory,
                    NamespaceName           = namespaceName,
                    GeneratedNamespaceName  = namespaceName.Combine(projectNamesInfo.GeneratedNamespaceSuffix),
                    RootNamespace           = projectNamespace,
                    Logger                  = new Logger(RootNamespace.Name),
                };
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
            var rootProject = new ProjectEnvironment
            {
                Directory              = projectNamesInfo.ProjectRootDirectory,
                NamespaceName          = projectNamesInfo.RootNamespaceName,
                GeneratedNamespaceName = generatedNamespaceName,
                RootNamespace          = RootNamespace,
                Logger                 = new Logger(RootNamespace.Name),
            };
            AddProject(rootProject, projectNamesInfo.CommonProjectNamespaceName);
            RootPseudoProject = rootProject;
        }
        else
        {
            RootPseudoProject = new ProjectEnvironmentData
            {
                Directory              = projectNamesInfo.ProjectRootDirectory,
                NamespaceName          = projectNamesInfo.RootNamespaceName,
                GeneratedNamespaceName = generatedNamespaceName,
                Logger                 = new Logger("Root"),
            };
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
        if (CancellationToken.IsCancellationRequested)
            return;

        var managerTasks = Administrators.Select(admin => admin.Collect());
        await Task.WhenAll(managerTasks);
        if (CancellationToken.IsCancellationRequested)
            return;

        RunCallbacks();
    }

    private void RunCallbacks()
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

    public readonly record struct GeneratedNamesInfo(string[] FileNames, 
        Dictionary<string, int> ExistingFileNamesToIndices, 
        HashSet<string> ConflictingNames)
    {
        const int USE_LONG_NAME = -1;

        public static GeneratedNamesInfo Create(ReadOnlySpan<CodeFragment> fragments, Logger logger)
        {
            string[] fileNames = new string[fragments.Length];
            Dictionary<string, int> existingFileNamesToIndices = new();
            HashSet<string> conflictingNames = new();

            for (int i = 0; i < fragments.Length; i++)
            {
                // This prevents fragments to try to be written in files with same names.
                if (existingFileNamesToIndices.TryGetValue(fragments[i].FileNameHint, out int index))
                {
                    // Has been reset, so always use the full name.
                    if (index != USE_LONG_NAME)
                    {
                        // The fragment at `index` tried to take the simple name, 
                        // so force them to use the long name.
                        existingFileNamesToIndices[fileNames[index]] = USE_LONG_NAME;
                        fileNames[index] = fragments[index].GetLongName();
                        conflictingNames.Add(fileNames[index]);
                    }

                    var longName = fragments[i].GetLongName();
                    // The long name has been taken too.
                    if (!conflictingNames.Add(longName))
                    {
                        // The long name here hopefully contains enough info to identify the plugin that caused the problem. 
                        logger.LogWarning($"The file name {longName} appeared twice. It will be appended a guid.");
                        // The guid is essentially guaranteed to never have a collision.
                        longName += Guid.NewGuid().ToString();
                    }

                    fileNames[i] = longName;
                }
                else
                {
                    // This simple name has not been taken, so use the simple name.
                    fileNames[i] = fragments[i].FileNameHint;
                    existingFileNamesToIndices.Add(fragments[i].FileNameHint, i);
                }
            }

            return new GeneratedNamesInfo(fileNames, existingFileNamesToIndices, conflictingNames);
        }
    
        public bool IsFileGenerated(string fileName)
        {
            // If it has been used directly.
            return (ExistingFileNamesToIndices.TryGetValue(fileName, out int index) 
                    // And not in its long form.
                    && index != USE_LONG_NAME)
                // Or it's the long name.
                || ConflictingNames.Contains(fileName);
        }

        
    }

    public static void RemoveNotGeneratedCodeFilesInDirectory(in GeneratedNamesInfo namesInfo, string directory)
    {
        foreach (string filePath in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(filePath);
            if (!namesInfo.IsFileGenerated(fileName))
                File.Delete(filePath);
        }
    }

    internal static async Task<bool> IsFileEqualToContent(SafeFileHandle fileHandle, ArraySegment<byte> bytes, CancellationToken cancellationToken)
    {
        long length = RandomAccess.GetLength(fileHandle);
        if (length != bytes.Count)
            return false;

        const int bufferSize = 1024 * 4; 
        byte[] readBytes = ArrayPool<byte>.Shared.Rent(bufferSize);
        long offset = 0;
        int difference;
        do
        {
            int bytesRead = await RandomAccess.ReadAsync(fileHandle, readBytes, offset, cancellationToken);
            difference = bytes.AsSpan()
                .Slice((int) offset, bytesRead)
                .SequenceCompareTo(
                    readBytes.AsSpan(0, bytesRead));
            if (difference != 0 || cancellationToken.IsCancellationRequested)
                break;
            offset += bytesRead;
        }
        while (offset < bytes.Count);

        ArrayPool<byte>.Shared.Return(readBytes);

        return difference == 0;
    }

    // Check if the content changed + write the new content
    internal static async Task WriteSingleCodeFileAsync(string outputFilePath, ArraySegment<byte> outputBytes, 
        bool whetherReturnToPool, CancellationToken cancellationToken)
    {
        using SafeFileHandle outputFileHandle = File.OpenHandle(outputFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);

        if (await IsFileEqualToContent(outputFileHandle, outputBytes, cancellationToken))
        {
            await RandomAccess.WriteAsync(outputFileHandle, outputBytes, 0, cancellationToken);
        }

        // WARNING: this line exists because ZString allocates from this pool
        if (whetherReturnToPool)
            ArrayPool<byte>.Shared.Return(outputBytes.Array);
    }

    public static Task ProcessProjectOutput(string outputDirectory, 
        ReadOnlySpan<CodeFragment> fragments, 
        Logger logger, CancellationToken cancellationToken)
    {
        // TODO: test if "" does not make it append /
        Debug.Assert(Directory.Exists(outputDirectory));
        var fileNamesInfo = GeneratedNamesInfo.Create(fragments, logger);
        Task[] writeOutputFileTasks = new Task[fragments.Length];

        for (int i = 0; i < fragments.Length; i++)
        {
            var outputFilePath = Path.Combine(outputDirectory, fileNamesInfo.FileNames[i]);
            writeOutputFileTasks[i] = WriteSingleCodeFileAsync(outputFilePath, fragments[i].Bytes, 
                fragments[i].AreBytesRentedFromArrayPool, cancellationToken);
        }

        RemoveNotGeneratedCodeFilesInDirectory(fileNamesInfo, outputDirectory);

        return Task.WhenAll(writeOutputFileTasks);
    }

    /// <summary>
    /// </summary>
    public Task WriteCodeFiles_SplitByProject(string generatedFolderRelativePath)
    {
        Task ProcessProject(ProjectEnvironmentData project)
        {
            // TODO: test if "" does not make it append /
            var outputDirectory = Path.Combine(project.Directory, generatedFolderRelativePath);
            var dirInfo = Directory.CreateDirectory(outputDirectory);
            var fragments = CollectionsMarshal.AsSpan(project.CodeFragments);

            return ProcessProjectOutput(outputDirectory, fragments, Logger, CancellationToken);
        }

        return Task.WhenAll(Projects.Append(RootPseudoProject).Select(ProcessProject));
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
