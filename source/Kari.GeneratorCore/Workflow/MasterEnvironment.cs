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
using System.Reflection;

namespace Kari.GeneratorCore.Workflow;

public readonly struct ProjectNamesInfo
{
    public string CommonProjectNamespaceName { get; init; } = "Common";
    public string GeneratedNamespaceSuffix { get; init; } = "Generated";
    public string RootNamespaceName { get; init; } = "";
    public string ProjectRootDirectory { get; init; } = "";
}

public static class ReflectedFileStreamHelpers
{
    // The method I need is internal.
    // System.IO.Strategies.FileStreamHelpers.SetFileLength(SafeFileHandle, long);
    public static readonly Action<SafeFileHandle, long> SetFileLength;

    static ReflectedFileStreamHelpers()
    {
        SetFileLength = typeof(FileStream).Assembly
            .GetType("System.IO.Strategies.FileStreamHelpers")
            .GetMethod("SetFileLength", BindingFlags.Static|BindingFlags.NonPublic)
            .CreateDelegate<Action<SafeFileHandle, long>>();
            
        Assert(SetFileLength is not null);
    }
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
    public IEnumerable<ProjectEnvironmentData> AllProjects
    {
        get
        {
            IEnumerable<ProjectEnvironmentData> result = Projects;
            if (RootPseudoProject is not null)
                return result.Append(RootPseudoProject);
            return result;
        }
    }
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
                    GeneratedNamespaceName  = namespaceName.Join(projectNamesInfo.GeneratedNamespaceSuffix),
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
            // var editorDirectory = Path.Join(projectDirectory, "Editor");
            // if (!Directory.Exists(editorDirectory))
            // {
            //     Logger.LogWarning($"Found an editor project {namespaceName}, but no `Editor` folder.");
            //     continue;
            // }
            // var editorEnvironment = new ProjectEnvironment(
            //     directory:      editorDirectory,
            //     namespaceName:  namespaceName.Combine("Editor"),
            //     rootNamespace:  editorProjectNamespace,
            //     fileWriter:     RootWriter.GetWriter(Path.Join(editorDirectory, GeneratedPath)));
                
            // AddProject(editorEnvironment);
        }
    }

    public void InitializePseudoProjects(in ProjectNamesInfo projectNamesInfo)
    {
        var generatedNamespaceName = projectNamesInfo.RootNamespaceName.Join(projectNamesInfo.GeneratedNamespaceSuffix);
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

    public async Task CollectSymbols(HashSet<string> independentNamespaceNames)
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

    public readonly record struct CallbackInfo(int Priority, System.Action Callback);

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

    public Task GenerateCodeFragments()
    {
        var managerTasks = Administrators.Select(admin => admin.Generate());
        return Task.WhenAll(managerTasks);
    }

    // Thought: I kinda want to split these methods into a static class.
    // Most of them don't need that much context, just requiring all projects, which I could pass as a parameter.
    public readonly record struct GeneratedFileNamesInfo(
        string[] FileNames, 
        Dictionary<string, int> ExistingFileNamesToIndices, 
        HashSet<string> ConflictingNames)
    {
        const int USE_LONG_NAME = -1;

        public static GeneratedFileNamesInfo Create(ReadOnlySpan<CodeFragment> fragments, Logger logger)
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

            return new GeneratedFileNamesInfo(fileNames, existingFileNamesToIndices, conflictingNames);
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

    internal static async Task<bool> IsFileEqualToContent(SafeFileHandle fileHandle, IEnumerable<ArraySegment<byte>> bytes, CancellationToken cancellationToken)
    {
        long length = RandomAccess.GetLength(fileHandle);
        if (length == 0 || bytes.TryGetNonEnumeratedCount(out int byteCount) && length != byteCount)
            return false;

        const int bufferSize = 1024 * 4 * 16; 
        byte[] readBytes = ArrayPool<byte>.Shared.Rent(bufferSize);
        long offsetInfoFile = 0;
        int difference = 0;
        int offsetIntoArraySegment = 0;
        var enumerator = bytes.GetEnumerator();

        while (true)
        {
            int bytesRead = await RandomAccess.ReadAsync(fileHandle, readBytes, offsetInfoFile, cancellationToken);
            if (bytesRead == 0)
                goto outer;

            int comparedBytesThisIterationTotal = 0;
            while (comparedBytesThisIterationTotal < bytesRead)
            {
                int slicedBytesCount = Math.Min(enumerator.Current.Count, bytesRead);
                
                difference = enumerator.Current.AsSpan()
                    .Slice((int) offsetInfoFile, slicedBytesCount)
                    .SequenceCompareTo(
                        readBytes.AsSpan(offsetIntoArraySegment, slicedBytesCount));

                comparedBytesThisIterationTotal += slicedBytesCount;
                offsetInfoFile += slicedBytesCount;

                if (offsetIntoArraySegment == enumerator.Current.Count)
                {
                    if (!enumerator.MoveNext() && offsetInfoFile < length)
                    {
                        difference = 1;
                        goto outer;
                    }
                    offsetIntoArraySegment = 0;
                }
                if (difference != 0 || cancellationToken.IsCancellationRequested)
                    goto outer;
            }
        }

        outer:
        ArrayPool<byte>.Shared.Return(readBytes);

        return difference == 0;
    }

    internal static async Task<bool> IsFileEqualToContent(SafeFileHandle fileHandle, ArraySegment<byte> bytes, long fromByteIndex, CancellationToken cancellationToken)
    {
        long length = RandomAccess.GetLength(fileHandle);
        if (length != bytes.Count)
            return false;

        const int bufferSize = 1024 * 4; 
        byte[] readBytes = ArrayPool<byte>.Shared.Rent(bufferSize);
        long offset = fromByteIndex;
        int difference;
        do
        {
            int bytesRead = await RandomAccess.ReadAsync(fileHandle, readBytes, offset, cancellationToken);
            if (bytesRead == 0)
            {
                difference = 1;
                break;
            }
            difference = bytes.AsSpan()
                .Slice((int) offset, bytesRead)
                .SequenceCompareTo(
                    readBytes.AsSpan(0, bytesRead));
            if (difference != 0 || cancellationToken.IsCancellationRequested)
                break;
            offset += bytesRead;
        }
        while (offset < bytes.Count + fromByteIndex);

        ArrayPool<byte>.Shared.Return(readBytes);

        return difference == 0;
    }

    // Check if the content changed + write the new content
    internal static async Task WriteSingleCodeFileAsync(string outputFilePath, ArraySegment<byte> outputBytes,
        CancellationToken cancellationToken)
    {
        using SafeFileHandle outputFileHandle = File.OpenHandle(outputFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);

        if (!await IsFileEqualToContent(outputFileHandle, outputBytes, 
            fromByteIndex: CodeFileCommon.HeaderBytes.Length, cancellationToken))
        {
            ReflectedFileStreamHelpers.SetFileLength(outputFileHandle, outputBytes.Count);

            long offset = 0;

            await RandomAccess.WriteAsync(outputFileHandle, CodeFileCommon.HeaderBytes, offset, cancellationToken);
            offset += CodeFileCommon.HeaderBytes.Length;
            
            await RandomAccess.WriteAsync(outputFileHandle, outputBytes, offset, cancellationToken);
            offset += outputBytes.Count;
            
            await RandomAccess.WriteAsync(outputFileHandle, CodeFileCommon.FooterBytes, offset, cancellationToken);
        }
    }

    internal static async Task WriteArraySegmentsToCodeFileAsync(string outputFilePath, 
        IEnumerable<ArraySegment<byte>> outputBytes, CancellationToken cancellationToken)
    {
        using SafeFileHandle outputFileHandle = File.OpenHandle(outputFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);

        // Since it's a single file, I'll just assume it will always change. Not worth it to check.
        // if (await IsFileEqualToContent(outputFileHandle, outputBytes, 
        //     fromByteIndex: CodeFileCommon.HeaderBytes.Length, cancellationToken))
        long offset = 0;
        foreach (var segment in outputBytes)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            await RandomAccess.WriteAsync(outputFileHandle, segment, offset, cancellationToken);
            offset += segment.Count;
        }
        
        // We might have to set the length ahead of time.
        ReflectedFileStreamHelpers.SetFileLength(outputFileHandle, offset);
    }

    /// <summary>
    /// Make sure the directory exists, before calling this.
    /// </summary>
    internal static Task WriteCodeFragmentsToSeparateFiles(string outputDirectory, 
        ReadOnlySpan<CodeFragment> fragments, 
        string[] fileNames, 
        CancellationToken cancellationToken)
    {
        // The directory existing is part of the contract.
        Debug.Assert(Directory.Exists(outputDirectory));

        Task[] writeOutputFileTasks = new Task[fragments.Length];

        for (int i = 0; i < fragments.Length; i++)
        {
            var outputFilePath = Path.Join(outputDirectory, fileNames[i]);
            writeOutputFileTasks[i] = WriteSingleCodeFileAsync(outputFilePath, fragments[i].Bytes, cancellationToken);
        }

        // TODO: this one should be done elsewhere.
        // RemoveNotGeneratedCodeFilesInDirectory(fileNamesInfo, outputDirectory);

        return Task.WhenAll(writeOutputFileTasks);
    }

    public readonly record struct GeneratedPathsInfo(
        string OutputDirectory, GeneratedFileNamesInfo GeneratedNamesInfo)
    {
        public void RemoveCodeFilesThatWereNotGenerated()
        {
            foreach (string filePath in Directory.EnumerateFiles(OutputDirectory, "*.cs", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(filePath);
                if (!GeneratedNamesInfo.IsFileGenerated(fileName))
                    File.Delete(filePath);
            }
        }   
    }

    public record class SingleDirectoryOutputResult(
        GeneratedPathsInfo GeneratedPaths, Task WriteOutputTask);

    /// <summary>
    /// </summary>
    public IEnumerable<SingleDirectoryOutputResult> WriteCodeFiles_NestedDirectory_ForEachProject(string generatedFolderRelativePath)
    {
        SingleDirectoryOutputResult ProcessProject(ProjectEnvironmentData project)
        {
            // TODO: test if "" does not make it append /
            var outputDirectory = Path.Join(project.Directory, generatedFolderRelativePath);
            CodeFileCommon.InitializeGeneratedDirectory(outputDirectory);
            var fragments = CollectionsMarshal.AsSpan(project.CodeFragments);
            var fileNamesInfo = GeneratedFileNamesInfo.Create(fragments, Logger);

            return new SingleDirectoryOutputResult(
                new GeneratedPathsInfo(outputDirectory, fileNamesInfo), 
                WriteCodeFragmentsToSeparateFiles(
                    outputDirectory, fragments, fileNamesInfo.FileNames, CancellationToken));
        }

        return AllProjects.Select(ProcessProject);
    }

    /// <summary>
    /// This method assumes:
    /// 1. The project names correspond to namespaces. 
    /// 2. The project names are unique.
    /// </summary>
    public IEnumerable<SingleDirectoryOutputResult> WriteCodeFiles_SingleDirectory_SplitByProject(string generatedFolderFullPath)
    {
        // Make sure the project names are unique.
        // This has to be enforced elsewhere tho, but for now do it here.
        #if DEBUG
        {
            var projectNames = new HashSet<string>();
            var duplicateNames = new List<string>();
            foreach (var p in Projects.Append(RootPseudoProject))
            {
                if (!projectNames.Add(p.NamespaceName))
                    duplicateNames.Add(p.NamespaceName);
            }
            Debug.Assert(duplicateNames.Count > 0, "There were projects with duplicate names: " + String.Join(", ", duplicateNames));
        }
        #endif

        SingleDirectoryOutputResult ProcessProject(ProjectEnvironmentData project)
        {
            var outputDirectory = Path.Join(generatedFolderFullPath, project.NamespaceName);
            CodeFileCommon.InitializeGeneratedDirectory(outputDirectory);
            var fragments = CollectionsMarshal.AsSpan(project.CodeFragments);
            var fileNamesInfo = GeneratedFileNamesInfo.Create(fragments, Logger);

            return new SingleDirectoryOutputResult(
                new GeneratedPathsInfo(outputDirectory, fileNamesInfo),
                WriteCodeFragmentsToSeparateFiles(
                    outputDirectory, fragments, fileNamesInfo.FileNames, CancellationToken));
        }

        return AllProjects.Select(ProcessProject);
    }

    public void DisposeOfAllCodeFragments()
    {
        foreach (var p in Projects)
            p.DisposeOfCodeFragments();
        RootPseudoProject.DisposeOfCodeFragments();
    }

    internal static IEnumerable<ArraySegment<byte>> GetProjectArraySegmentsForSingleFileOutput(ProjectEnvironmentData project)
    {
        yield return CodeFileCommon.SlashesSpaceBytes;
        yield return Encoding.UTF8.GetBytes(project.NamespaceName);
        yield return CodeFileCommon.NewLineBytes;
        yield return CodeFileCommon.NewLineBytes;

        for (int i = 0; i < project.CodeFragments.Count; i++)
        {
            yield return CodeFileCommon.SlashesSpaceBytes;
            yield return Encoding.UTF8.GetBytes(project.CodeFragments[i].FileNameHint);
            yield return CodeFileCommon.SpaceBytes;
            yield return Encoding.UTF8.GetBytes(project.CodeFragments[i].NameHint);
            yield return CodeFileCommon.NewLineBytes;

            yield return project.CodeFragments[i].Bytes;
        }
    }

    internal static IEnumerable<ArraySegment<byte>> WrapArraySegmentsWithHeaderAndFooter(IEnumerable<ArraySegment<byte>> segments)
    {
        return new ArraySegment<byte>[] { CodeFileCommon.HeaderBytes }
            .Concat(segments)
            .Append(CodeFileCommon.FooterBytes); 
    }
    
    public Task WriteCodeFiles_SingleFile(string singleOutputFileFullPath)
    {
        var directory = Path.GetDirectoryName(singleOutputFileFullPath);
        Directory.CreateDirectory(directory);

        foreach (var project in AllProjects)
        {
            project.CodeFragments.Sort();
        }

        return WriteArraySegmentsToCodeFileAsync(
            singleOutputFileFullPath, 
            WrapArraySegmentsWithHeaderAndFooter(
                AllProjects.SelectMany(GetProjectArraySegmentsForSingleFileOutput)), 
            CancellationToken);
    }

    public Task WriteCodeFiles_SingleFile_PerProject(string singleOutputFileRelativeToProjectDirectoryPath)
    {
        Task ProcessProject(ProjectEnvironmentData project)
        {
            project.CodeFragments.Sort();
            string outputFilePath = Path.Join(project.Directory, singleOutputFileRelativeToProjectDirectoryPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));
            return WriteArraySegmentsToCodeFileAsync(
                outputFilePath,
                WrapArraySegmentsWithHeaderAndFooter(GetProjectArraySegmentsForSingleFileOutput(project)),
                CancellationToken);
        }

        return Task.WhenAll(AllProjects.Select(ProcessProject));
    }

    public enum OutputMethod
    {
        Invalid = 0,
        SingleFile = 1, // requires absolute file path
        SingleDirectory_SplitByProject = 2, // requres dir name
        NestedDirectory_ForEachProject = 3, // requres dir name
        SingleFile_PerProject = 4, // requres file name
    }
}
