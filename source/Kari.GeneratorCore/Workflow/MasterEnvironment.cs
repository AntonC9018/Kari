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
using System.Collections;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kari.GeneratorCore.Workflow;

// Class because we use it in an async context, and those do not work with the in parameter.
public record class ProjectNamesInfo
{
    public string CommonProjectNamespaceName { get; init; } = "Common";
    public string GeneratedNamespaceSuffix { get; init; } = "Generated";
    public string RootNamespaceName { get; init; } = "";
    public string ProjectRootDirectory { get; init; } = "";
    public string GeneratedName { get; init; } = "Generated"; // can be effectively determined from OutputMode
    public MasterEnvironment.OutputMode OutputMode { get; init; } // 
    /// <summary>
    /// Should include the generated directory name, if the output mode is set to nested.
    /// </summary>
    public List<string> IgnoredNames { get; init; }
    /// <summary>
    /// Should include the generated directory name, if the output mode is set to central.
    /// </summary>
    public List<string> IgnoredFullPaths { get; init; }
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

public record struct OutputInfo(object GeneratedNames, MasterEnvironment.OutputMode OutputMode)
{
    // Contains the generated directory names / generated file names for each project.
    // If this is a list, then the output mode is per project,
    // if this is a string, then it is central, and the name indicates the file name / dir name.
}

public class MasterEnvironment : Singleton<MasterEnvironment>
{
    [System.Flags]
    public enum OutputMode
    {
        [HideOption] Central = 1,
        [HideOption] File = 2,

        CentralFile = Central | File, // requires absolute file path
        CentralDirectory = Central, // requires dir name
        NestedFile = File, // requires file name
        NestedDirectory = 0, // requires dir name
    }

    [System.Flags]
    public enum InputType
    {
        Autodetect,
        MSBuild,
        UnityAsmdefs,
        Monolithic,
        ByDirectory,
    }

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

    public readonly NamedLogger Logger;
    public readonly CancellationToken CancellationToken;
    public ProjectEnvironment[] Projects { get; private set; }
    public OutputInfo OutputInfo { get; private set; }


    public IEnumerable<ProjectEnvironmentData> AllProjects => Projects;
    public readonly List<IAdministrator> Administrators = new List<IAdministrator>(5);

    /// <summary>
    /// Initializes the MasterEnvironment and replaces the global singleton instance.
    /// </summary>
    public MasterEnvironment(CancellationToken cancellationToken, NamedLogger logger)
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
            NamedLogger.LogPlain($"\nShowing help for `{admin}`.");
            parser.LogHelpFor(admin.GetArgumentObject());
        }
    }

    // TODO: maybe allow specifying the lang version??
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(
        LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Regular);
    private static readonly CSharpCompilationOptions CompilationOptions = new CSharpCompilationOptions(
            outputKind: OutputKind.DynamicallyLinkedLibrary, 
            allowUnsafe: true,
            reportSuppressedDiagnostics: false,
            concurrentBuild: true,
            generalDiagnosticOption: ReportDiagnostic.Suppress);

    private class SymbolNameComparer : IComparer<INamedTypeSymbol>
    {
        public static SymbolNameComparer Instance = new SymbolNameComparer();
        private StringComparer comparer = StringComparer.Ordinal;
        public int Compare(INamedTypeSymbol x, INamedTypeSymbol y)
        {
            return comparer.Compare(x.Name, y.Name);
        }
    }

    private readonly record struct DirectoriesAndNames(string[] Directories, string[] Names);

    public async Task InitializeCompilation(ProjectNamesInfo projectNamesInfo, OutputMode outputMode, InputType inputType)
    {
        // 1. msbuild input (ignore for now)
        // 2. simple directory based input (generate just a single directory, at least for now)
        // 3. unity (asmdefs)

        // directory (default), 
        // unity (detected by looking for asmdefs), 
        // msbuild (detected by looking for slns/csproj), ??? maybe not allow this one ???
        // Allow override too

        (string[] projectDirectories, string[] projectNames) = GetProjectDirectoriesAndNames(projectNamesInfo, Logger, ref inputType);
        // This is essentially error code error checking.
        if (Logger.AnyHasErrors)
            return;

        // The function should always return at least one directory
        Assert(projectDirectories is not null && projectNames is not null);

        int projectCount = projectDirectories.Length;

        // We keep at least the given directory in any case.
        Assert(projectCount > 0 
            // 1 name per project too.
            && projectNames.Length == projectCount);

        static DirectoriesAndNames GetProjectDirectoriesAndNames(
            ProjectNamesInfo projectNamesInfo, NamedLogger logger, ref InputType inputType)
        {
            switch (inputType)
            {
                case InputType.Autodetect:
                {
                    var asmdefs = Directory.GetFiles(projectNamesInfo.ProjectRootDirectory, "*.asmdef", SearchOption.AllDirectories);
                
                    if (asmdefs.Length > 0)
                    {
                        inputType = InputType.UnityAsmdefs;
                        return GetDirectoriesAndNamesUnity(asmdefs, logger);
                    }
                    // If there are files at root, we assume it's a monolithic project
                    else if (Directory.EnumerateFiles(projectNamesInfo.ProjectRootDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                        // Filter out the files that are ignored (in case it's a single generated file in root)
                        // TODO: think of all potential edge cases here to maybe simplify this logic.
                        // NOTE: We can ignore the generated names, because it's not a nested directory.
                        .Where(fileFullPath => !projectNamesInfo.IgnoredFullPaths.Any(t => fileFullPath.StartsWith(t)))
                        // The presense of even a single file forces this to use one-project mode.
                        .Any())
                    {
                        logger.LogInfo("The project has root cs files, hence it will be considered monolithic.");
                        inputType = InputType.Monolithic;
                        return GetDirectoriesAndNamesMonolithic(projectNamesInfo.ProjectRootDirectory);
                    }
                    // 
                    else
                    {
                        inputType = InputType.ByDirectory;
                        return GetDirectoriesSplitByDirectory(projectNamesInfo.ProjectRootDirectory);
                    }
                }

                case InputType.UnityAsmdefs:
                {
                    var asmdefs = Directory.GetFiles(projectNamesInfo.ProjectRootDirectory, "*.asmdef", SearchOption.AllDirectories);
                    return GetDirectoriesAndNamesUnity(asmdefs, logger);
                }

                case InputType.Monolithic:
                {
                    // The input type specified explicitly, hence we don't inform here.
                    return GetDirectoriesAndNamesMonolithic(projectNamesInfo.ProjectRootDirectory);
                }

                case InputType.ByDirectory:
                // For now don't do anything special for msbuild. 
                // But ideally, use the Rolsyn API's that parse their files properly (maybe).
                case InputType.MSBuild:
                {
                    return GetDirectoriesSplitByDirectory(projectNamesInfo.ProjectRootDirectory);
                }

                default:
                {
                    Assert(false);
                    throw null;
                }
            }

            static ReadOnlySpan<char> DeduceProjectNameFromDirectory(ReadOnlySpan<char> directoryFullPath)
            {
                var a = directoryFullPath.LastIndexOf(Path.DirectorySeparatorChar);
                Assert(a != -1, "Path must not be relative, hence at least one slash must be present.");
                return directoryFullPath.Slice(a + 1, directoryFullPath.Length - a);
            }

            static string[] DeduceProjectNamesDefault(string[] directoryFullPaths)
            {
                var result = new string[directoryFullPaths.Length];
                for (int i = 0; i < result.Length; i++)
                    result[i] = DeduceProjectNameFromDirectory(directoryFullPaths[i]).ToString();
                return result;
            }

            static DirectoriesAndNames GetDirectoriesAndNamesUnity(string[] asmdefs, NamedLogger logger)
            {
                var directories = new string[asmdefs.Length];
                for (int i = 0; i < asmdefs.Length; i++)
                    directories[i] = Path.GetDirectoryName(asmdefs[i]);
                
                // Nesting asmdefs is never allowed
                if (directories.Distinct().Count() == directories.Length)
                {
                    logger.LogError("Duplicate project folders were detected. Cannot continue.");
                }

                string[] names = new string[asmdefs.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    // TODO: Can it have a project name field?? or is it encoded in the file name??
                    // Not sure if needed
                    // var asmdefJson = Object.Parse(File.ReadAllText(asmdef));

                    // So just get the name of the file ??????
                    names[i] = Path.GetFileNameWithoutExtension(asmdefs[i]);
                }

                return new DirectoriesAndNames(directories, names);
            }

            static DirectoriesAndNames GetDirectoriesAndNamesMonolithic(string projectRootDirectory)
            {
                return new DirectoriesAndNames(new[] { projectRootDirectory }, new[] { "Root" });
            }

            static DirectoriesAndNames GetDirectoriesSplitByDirectory(string projectRootDirectory)
            {
                var dirs = Directory.GetDirectories(projectRootDirectory, "*", SearchOption.TopDirectoryOnly);
                return new DirectoriesAndNames(dirs, DeduceProjectNamesDefault(dirs));
            }
        }
        
        // An array of X for every asmdef
        // X = a list of syntax trees for every source file.

        var syntaxTreeTasksLists = new List<Task<SyntaxTree>>[projectCount];

        static Task<SyntaxTree> StartParseTask(string text, string filePath, CancellationToken token)
        {
            return Task.Run(() => CSharpSyntaxTree.ParseText(text, ParseOptions, filePath), token);
        }

        for (int i = 0; i < projectCount; i++)
        {
            var listOfTasks = new List<Task<SyntaxTree>>();
            syntaxTreeTasksLists[i] = listOfTasks;
            var directoryPath = projectDirectories[i];
            var pathPrefixLength = directoryPath.Length + 1;

            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories))
            {
                if (CancellationToken.IsCancellationRequested)
                    return;
                if (ShouldIgnoreFile(filePath, pathPrefixLength, projectNamesInfo.IgnoredNames, projectNamesInfo.IgnoredFullPaths))
                    continue;

                var text = await File.ReadAllTextAsync(filePath);
                var task = StartParseTask(text, filePath, CancellationToken);
                listOfTasks.Add(task);

                static bool ShouldIgnoreFile(
                    ReadOnlySpan<char> fullFilePath, int pathPrefixLength, 
                    List<string> ignoredNames, List<string> ignoredFullPaths)
                {
                    var len = fullFilePath.Length - pathPrefixLength;
                    var relativeFilePath = fullFilePath.Slice(pathPrefixLength, len);
                    // TODO: 
                    // This ignores both directories and files.
                    // So like adding `bin` to the ignore list also implicitly adds `bin.cs` to the ignore list.
                    // I'm not sure if this is how it should be. 
                    // It's unlikely anyone would name files the same though.
                    // But I also doubt anyone would want to ignore anything but directories... so idk.
                    foreach (var ignoredName in ignoredNames)
                    {
                        if (relativeFilePath.StartsWith(ignoredName, StringComparison.Ordinal))
                            return true;
                    }
                    foreach (var ignoredFullPath in ignoredFullPaths)
                    {
                        if (fullFilePath.StartsWith(ignoredFullPath, StringComparison.Ordinal))
                            return true;
                    }
                    return false;
                }
            }
        }
        var annotationSyntaxTreeTasks = new Task<SyntaxTree>[Administrators.Count];
        for (int i = 0; i < Administrators.Count; i++)
        {
            var text = Administrators[i].GetAnnotations();
            var task = StartParseTask(text, filePath: null, CancellationToken);
            annotationSyntaxTreeTasks[i] = task;
        }

        int numSyntaxTrees = syntaxTreeTasksLists.Sum(a => a.Count);
        SyntaxTree[] syntaxTrees = new SyntaxTree[numSyntaxTrees + Administrators.Count];
        {
            int syntaxTreeGlobalIndex = 0;
            foreach (var syntaxTreeTasks in syntaxTreeTasksLists)
            foreach (var syntaxTreeTask in syntaxTreeTasks)
            {
                syntaxTrees[syntaxTreeGlobalIndex] = await syntaxTreeTask;
                syntaxTreeGlobalIndex++;
            }
            foreach (var annotationTask in annotationSyntaxTreeTasks)
            {
                syntaxTrees[syntaxTreeGlobalIndex] = await annotationTask;
                syntaxTreeGlobalIndex++;
            }
        }

        var standardMetadataType = new[]
        {
            typeof(object),
            typeof(Attribute),
        };
        var metadata = standardMetadataType
            .Select(t => t.Assembly.Location)
            .Distinct()
            .Select(t => MetadataReference.CreateFromFile(t));

        var compilation = CSharpCompilation.Create("Kari", syntaxTrees, metadata, CompilationOptions);

        var symbolCollectionTasks = new Task<INamedTypeSymbol[]>[projectCount];
        {
            int currentSyntaxTreeStartIndex = 0;

            for (int projectIndex = 0; projectIndex < projectCount; projectIndex++)
            {
                var syntaxTreeCount = syntaxTreeTasksLists[projectIndex].Count;
                var trees = new ArraySegment<SyntaxTree>(syntaxTrees, currentSyntaxTreeStartIndex, syntaxTreeCount);
                symbolCollectionTasks[projectIndex] = Task.Run(() => Collect(trees, compilation));
                
                static async Task<INamedTypeSymbol[]> Collect(
                    ArraySegment<SyntaxTree> syntaxTrees, Compilation compilation)
                {
                    var result = new HashSet<INamedTypeSymbol>();
                    foreach (var syntaxTree in syntaxTrees)
                    {
                        var root = await syntaxTree.GetRootAsync();
                        var model = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
                        foreach (var tds in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                        {
                            var s = model.GetDeclaredSymbol(tds);
                            result.Add(s);
                        }
                    }
                    var list = result.ToArray();
                    Array.Sort(list, SymbolNameComparer.Instance);
                    return list;
                }
            }
        }

        var collectedSymbols = new INamedTypeSymbol[projectCount][];
        for (int projectIndex = 0; projectIndex < projectCount; projectIndex++)
        {
            collectedSymbols[projectIndex] = await symbolCollectionTasks[projectIndex];
        }

        Symbols.Initialize(Compilation);
        Compilation = Compilation;
        RootNamespace = Compilation.TryGetNamespace(projectNamesInfo.RootNamespaceName);

        if (RootNamespace is null)
            Logger.LogError($"No such root namespace `{projectNamesInfo.RootNamespaceName}`");
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

    public OutputInfo[] FindProjects(in ProjectNamesInfo projectNamesInfo, bool treatEditorAsSubproject)
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
                    Logger.LogError($"The namespace defined by {asmdef} must be a string.");
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
                    Logger                  = new NamedLogger(RootNamespace.Name),
                };
                // TODO: Assume no duplicates for now, but this will have to be error-checked.
                AddProject(environment, projectNamesInfo.CommonProjectNamespaceName);
            }
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
                Logger                 = new NamedLogger(RootNamespace.Name),
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
                Logger                 = new NamedLogger("Root"),
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

        public static GeneratedFileNamesInfo Create(ReadOnlySpan<CodeFragment> fragments, NamedLogger logger)
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

    internal static async Task<bool> IsFileEqualToContent(SafeFileHandle fileHandle, ArraySegment<byte> bytes, int byteOffsetFromStart, int byteOffsetFromEnd, CancellationToken cancellationToken)
    {
        long length = RandomAccess.GetLength(fileHandle);
        if (length - byteOffsetFromEnd - byteOffsetFromStart != bytes.Count)
            return false;

        const int bufferSize = 1024 * 4; 
        byte[] readBytes = ArrayPool<byte>.Shared.Rent(bufferSize);
        long offset = 0;
        int difference;
        do
        {
            int bytesRead = await RandomAccess.ReadAsync(fileHandle, readBytes, offset + byteOffsetFromStart, cancellationToken);
            int sliceCompareLength = Math.Min(bytesRead, bytes.Count - (int) offset);
            difference = bytes.AsSpan()
                .Slice((int) offset, sliceCompareLength)
                .SequenceCompareTo(
                    readBytes.AsSpan(0, sliceCompareLength));
            
            if (sliceCompareLength < bytesRead
                || difference != 0 
                || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            offset += sliceCompareLength;
        }
        while (offset < bytes.Count + byteOffsetFromStart);

        ArrayPool<byte>.Shared.Return(readBytes);

        return difference == 0;
    }

    // Check if the content changed + write the new content
    internal static async Task WriteSingleCodeFileAsync(string outputFilePath, ArraySegment<byte> outputBytes,
        CancellationToken cancellationToken)
    {
        using SafeFileHandle outputFileHandle = File.OpenHandle(outputFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        WriteLine(outputFilePath);
        if (!await IsFileEqualToContent(outputFileHandle, outputBytes, 
            byteOffsetFromStart:    CodeFileCommon.HeaderBytes.Length,
            byteOffsetFromEnd:      CodeFileCommon.FooterBytes.Length,
            cancellationToken))
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
    public SingleDirectoryOutputResult[] WriteCodeFiles_NestedDirectory(string generatedFolderRelativePath)
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

        return AllProjects.Select(ProcessProject).ToArray();
    }

    /// <summary>
    /// This method assumes:
    /// 1. The project names correspond to namespaces. 
    /// 2. The project names are unique.
    /// </summary>
    public SingleDirectoryOutputResult[] WriteCodeFiles_CentralDirectory(string generatedFolderFullPath)
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

        return AllProjects.Select(ProcessProject).ToArray();
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
    
    public Task WriteCodeFiles_CentralFile(string singleOutputFileFullPath)
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

    public Task WriteCodeFiles_SingleNestedFile(string singleOutputFileRelativeToProjectDirectoryPath)
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
}
