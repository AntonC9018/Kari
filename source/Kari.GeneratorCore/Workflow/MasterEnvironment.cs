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
    public string GeneratedNamespaceSuffix { get; init; } = "Generated";
    public string RootNamespaceName { get; init; } = "";
    public string ProjectRootDirectory { get; init; } = "";


    /*
        Scenarios: 
        
        - Unity + generate directly into project root + this set to default.

            Creates either one cs file that will be ignored due to the output mode, or
            Creates a Generated directory with the output files, that will not affect it.


            Caveat: 
            
            Need to consider the pseudoproject, whether it is among other projects already.

            If RootPseudoProjectDirectory is not "", then it should point to a folder into 
            which the files will go, or which would contain the asmdef for the root project.
            In case it points to a folder, we should consider it a project, otherwise we should not.
            So we need a bool to tell whether it is generated or not.
            Or even better, store an index, which would be -1 if it is not within projects.

            If it is "", we need to always consider it separately. It won't be within other folders.


        - Directory-based.

            If this is set to default, same thing as with unity, that is,
            it is considered separately from other projects, and the code is generated separately also.

            If this is not the default, it is searched within the other projects,
            that is, it is not separate. 
            And the root project is scanned for types too (but the generated files are not loaded).


        - Monolithic.

            If set to default, find within projects.

            If not default, generate into that folder.
            An edge-case here is the generated files being considered as source files, in case this directory
            is not ignored, so it must be explicitly ignored. (This whole directory).

    */
    /// <summary>
    /// Where to generate the root files, in case a root project is not found.
    /// You do not have to add it into the IgnoredNames list, it will add it 
    /// there automatically if the InputType is set to Adaptive or Monolithic.
    /// 
    /// If such a project with such a name is not found, it will use a folder with this name instead.
    ///
    /// It is in most cases ok leaving this at default.
    /// </summary>
    public string RootPseudoProjectName { get; init; } = "";

    /// <summary>
    /// Same story as with `RootPseudoProjectName`, see that one.
    /// </summary>
    public string CommonPseudoProjectName { get; init; } = "";

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
    public enum InputMode
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
    /// Is -1 if it is not a real project within the Project list.
    /// </summary>
    public int CommonPseudoProjectIndex { get; private set; }
    
    /// <summary>
    /// Holds any "master" or "runner" code.
    /// This is the only project that can reference all other projects.
    /// Place functions that bring together all generated code here, 
    /// like registering classes in a central registry, giving them id's etc.
    /// </summary>
    public ProjectEnvironmentData RootPseudoProject { get; private set; }

    /// <summary>
    /// Is -1 if it is not a real project within the Project list.
    /// If it is not -1, you can get the cached symbols for the given project
    /// by indexing the Projects list with this.
    /// </summary>
    public int RootPseudoProjectIndex { get; private set; }

    /// <summary>
    /// All symbols must come from this central compilation.
    /// </summary>
    public Compilation Compilation { get; private set; }


    private readonly NamedLogger Logger;
    public readonly CancellationToken CancellationToken;

    
    // public readonly record struct ProjectInfos(
    //     string[] Names,
    //     string[] GeneratedNamespaceNames,
    //     string[] Directories,
    //     SyntaxTree[][] SyntaxTreeArrays,
    //     SyntaxTree[] AnnotationsSyntaxTrees,
    //     List<CodeFragment>[] CodeFragments)
    // {
    // }


    /// <summary>
    /// Projects whose code will be analysed.
    /// Set these with `InitializeCompilation`.
    /// </summary>
    public ProjectEnvironment[] Projects { get; private set; }

    public IEnumerable<ProjectEnvironmentData> AllProjectDatas {
        get
        {
            foreach (var p in Projects)
                yield return p.Data;
            if (RootPseudoProjectIndex == -1)
                yield return RootPseudoProject;
            if (CommonPseudoProjectIndex == -1 && !ReferenceEquals(RootPseudoProject, CommonPseudoProject))
                yield return CommonPseudoProject;
        }   
    }

    public readonly List<IAdministrator> Administrators = new List<IAdministrator>(8);

    /// <summary>
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

    private record struct ProjectDataAndIndex(ProjectEnvironmentData Data, int Index);
    private record struct ProjectDatas(ProjectEnvironmentData[] Projects, ProjectDataAndIndex Root, ProjectDataAndIndex Common);

    /// <summary>
    /// Resolves projects given by `projectNamesInfo`, using InputType.
    /// Returns a Task that resolves when all the source file have been loaded, 
    /// and all the essential symbols have been cached. 
    /// </summary>
    public async Task InitializeCompilation(ProjectNamesInfo projectNamesInfo, InputMode inputType)
    {
        // 1. msbuild input (ignore for now)
        // 2. simple directory based input (generate just a single directory, at least for now)
        // 3. unity (asmdefs)

        // directory (default), 
        // unity (detected by looking for asmdefs), 
        // msbuild (detected by looking for slns/csproj), ??? maybe not allow this one ???
        // Allow override too

        ProjectDatas projectDatas = GetProjectDirectoriesAndNames(projectNamesInfo, Logger, ref inputType);
        // This is essentially error code error checking.
        if (Logger.AnyHasErrors)
            return;

        // The function should always return at least one directory
        Assert(projectDatas.Projects is not null);

        int projectCount = projectDatas.Projects.Length;

        // We keep at least the given directory in any case.
        Assert(projectCount > 0);


        static ProjectDatas GetProjectDirectoriesAndNames(
            ProjectNamesInfo projectNamesInfo, NamedLogger logger, ref InputMode inputType)
        {
            switch (inputType)
            {
                case InputMode.Autodetect:
                {
                    var asmdefs = Directory.GetFiles(projectNamesInfo.ProjectRootDirectory, "*.asmdef", SearchOption.AllDirectories);
                
                    if (asmdefs.Length > 0)
                    {
                        inputType = InputMode.UnityAsmdefs;
                        return GetProjectsUnity(projectNamesInfo, asmdefs, logger);
                    }
                    // If there are files at root, we assume it's a monolithic project
                    else if (Directory.EnumerateFiles(projectNamesInfo.ProjectRootDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                        // Filter out the files that are ignored (in case it's a single generated file in root)
                        // TODO: Think of all potential edge cases here to maybe simplify this logic.
                        // NOTE: We can disregard the generated names, because it's not a nested directory.
                        .Where(fileFullPath => !projectNamesInfo.IgnoredFullPaths.Any(t => fileFullPath.StartsWith(t)))
                        // The presence of even a single file forces this to use one-project mode.
                        .Any())
                    {
                        logger.LogInfo("The project has root cs files, hence it will be considered monolithic.");
                        inputType = InputMode.Monolithic;
                        return GetProjectsMonolithic(projectNamesInfo);
                    }
                    // 
                    else
                    {
                        inputType = InputMode.ByDirectory;
                        return GetProjectsByDirectory(projectNamesInfo);
                    }
                }

                case InputMode.UnityAsmdefs:
                {
                    var asmdefs = Directory.GetFiles(projectNamesInfo.ProjectRootDirectory, "*.asmdef", SearchOption.AllDirectories);
                    return GetProjectsUnity(projectNamesInfo, asmdefs, logger);
                }

                case InputMode.Monolithic:
                {
                    // The input type specified explicitly, hence we don't inform here.
                    return GetProjectsMonolithic(projectNamesInfo);
                }

                case InputMode.ByDirectory:
                // For now don't do anything special for msbuild. 
                // But ideally, use the Rolsyn API's that parse their files properly (maybe).
                case InputMode.MSBuild:
                {
                    inputType = InputMode.ByDirectory;
                    return GetProjectsByDirectory(projectNamesInfo);
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

            static ProjectDatas GetProjectsUnity(ProjectNamesInfo projectNamesInfo, string[] asmdefs, NamedLogger logger)
            {
                var result = new ProjectDatas();

                var projects = asmdefs
                    .Select(asmdefFullPath => 
                    {
                        var directory = Path.GetDirectoryName(asmdefFullPath);

                        // TODO: Can it have a project name field?? or is it encoded in the file name??
                        // Not sure if needed
                        // var asmdefJson = Object.Parse(File.ReadAllText(asmdef));
                        // So just get the name of the file ??????
                        var name = Path.GetFileNameWithoutExtension(asmdefFullPath);

                        return CreateProjectWithDefaultNamespace(name, directory, projectNamesInfo.GeneratedNamespaceSuffix); 
                    })
                    .ToArray();

                result.Projects = projects;
                (result.Common, result.Root) = FindOrCreateCommonAndRootProjects(projectNamesInfo, projects);
                
                // Error checking
                // TODO: 
                // Make this optional, controlled with a flag.
                // Reasoning: this is enforced by unity already.
                {
                    foreach (var p in projects)
                    {
                        if (p.DirectoryFullPath == projectNamesInfo.ProjectRootDirectory)
                        {
                            logger.LogError($"The asmdef at {p.DirectoryFullPath} is in the root directory, which is not allowed.");
                        }
                    }

                    // TODO: Nesting asmdefs is never allowed
                    // if (directories.Distinct().Count() == directories.Length)
                    // {
                    //     logger.LogError("Duplicate project folders were detected. Cannot continue.");
                    // }
                    // TODO: somehow use readonly spans??
                    HashSet<string> starts = new HashSet<string>();
                    int rootDirLength = projectNamesInfo.ProjectRootDirectory.Length;
                    for (int i = 0; i < projects.Length; i++)
                    {
                        var project = projects[i];
                        var relative = project.DirectoryFullPath.AsSpan(rootDirLength, project.DirectoryFullPath.Length - rootDirLength);
                        var indexOfSlash = relative.LastIndexOf(Path.DirectorySeparatorChar);
                        
                        // If it's in the root directory, this will be empty.
                        // Already reported this above.
                        if (indexOfSlash != -1)
                            continue;
                        
                        var directoriesSubstring = relative.Slice(0, indexOfSlash).ToString();
                        
                        if (!starts.Add(directoriesSubstring))
                        {
                            logger.LogError($"Nested project detected at {project.DirectoryFullPath} (nested within {directoriesSubstring}).");
                        }
                    }
                }

                return result;
            }

            static ProjectEnvironmentData CreateProjectWithDefaultNamespace(string name, string path, string suffix)
            {
                return new ProjectEnvironmentData(name, path, name.Join(suffix));
            }

            static ProjectDatas GetProjectsMonolithic(ProjectNamesInfo projectNamesInfo)
            {
                var result = new ProjectDatas();

                string projectName = "Root";
                if (projectNamesInfo.RootPseudoProjectName != "")
                    projectName = projectNamesInfo.RootPseudoProjectName;

                var project = CreateProjectWithDefaultNamespace(
                    projectName, 
                    projectNamesInfo.ProjectRootDirectory, 
                    projectNamesInfo.GeneratedNamespaceSuffix);

                result.Projects = new[] { project };
                result.Root = new (project, 0);
                
                if (projectNamesInfo.CommonPseudoProjectName == "" ||
                    projectNamesInfo.CommonPseudoProjectName == projectName)
                {
                    result.Common = new (project, 0);
                }
                else
                {
                    var directory = Path.Join(projectNamesInfo.ProjectRootDirectory, projectNamesInfo.CommonPseudoProjectName);
                    var commonProject = CreateProjectWithDefaultNamespace(
                        projectNamesInfo.CommonPseudoProjectName,
                        directory,
                        projectNamesInfo.GeneratedNamespaceSuffix);
                        
                    result.Common = new (commonProject, -1);

                    // The common project is an output-only project.
                    // projectNamesInfo.IgnoredFullPaths.Add(directory);
                }

                return result;
            }

            static ProjectDatas GetProjectsByDirectory(ProjectNamesInfo projectNamesInfo)
            {
                var result = new ProjectDatas();

                var projects = Directory.EnumerateDirectories(projectNamesInfo.ProjectRootDirectory, "*", SearchOption.TopDirectoryOnly)
                    .Select(directory =>
                    {
                        var projectName = DeduceProjectNameFromDirectory(directory).ToString();
                        return CreateProjectWithDefaultNamespace(projectName, directory, projectNamesInfo.GeneratedNamespaceSuffix);
                    })
                    .ToArray();

                result.Projects = projects;
                (result.Common, result.Root) = FindOrCreateCommonAndRootProjects(projectNamesInfo, projects);
                return result;
            }

            static (ProjectDataAndIndex Common, ProjectDataAndIndex Root) FindOrCreateCommonAndRootProjects(
                    ProjectNamesInfo projectNamesInfo, ProjectEnvironmentData[] projects)
            {
                var root = GetOrCreateRootProject();
                var common =
                    (projectNamesInfo.RootPseudoProjectName == projectNamesInfo.CommonPseudoProjectName)
                        ? root
                        : GetOrCreateCommonProject();

                return (common, root);

                ProjectDataAndIndex GetOrCreateRootProject()
                {
                    if (projectNamesInfo.RootPseudoProjectName == "")
                    {
                        var rootProject = CreateProjectWithDefaultNamespace(
                            "Root",
                            projectNamesInfo.ProjectRootDirectory,
                            projectNamesInfo.GeneratedNamespaceSuffix);
                        return new (rootProject, -1);
                    }
                    int indexOfRootProject = projects.IndexOfFirst(p => p.Name == projectNamesInfo.RootPseudoProjectName);
                    if (indexOfRootProject == -1)
                    {
                        var rootProject = CreateProjectWithDefaultNamespace(
                            projectNamesInfo.RootPseudoProjectName,
                            Path.Join(projectNamesInfo.ProjectRootDirectory, projectNamesInfo.RootPseudoProjectName),
                            projectNamesInfo.GeneratedNamespaceSuffix);

                        return new (rootProject, -1);
                    }
                    // Found the project within the project list.
                    return new (projects[indexOfRootProject], indexOfRootProject);
                }

                ProjectDataAndIndex GetOrCreateCommonProject()
                {
                    string searchedName = projectNamesInfo.CommonPseudoProjectName;
                    if (searchedName == "")
                    {
                        // if it already exists it will be a project already, so we could find it perhaps.
                        searchedName = "Common";
                    }
                    int indexOfRootProject = projects.IndexOfFirst(p => p.Name == searchedName);
                    if (indexOfRootProject == -1)
                    {
                        var rootProject = CreateProjectWithDefaultNamespace(
                            searchedName,
                            Path.Join(projectNamesInfo.ProjectRootDirectory, searchedName),
                            projectNamesInfo.GeneratedNamespaceSuffix);

                        return new (rootProject, -1);
                    }
                    // Found the project within the project list.
                    return new (projects[indexOfRootProject], indexOfRootProject);
                }
            }
        }
        
        // An array of X for every asmdef
        // X = a list of syntax trees for every source file.

        var syntaxTreeTasksLists = new List<Task<SyntaxTree>>[projectCount];
        var annotationSyntaxTreeTasks = new Task<SyntaxTree>[Administrators.Count];

        // Read all source files + start the parse tasks.
        {
            static Task<SyntaxTree> StartParseTask(string text, string filePath, CancellationToken token)
            {
                return Task.Run(() => CSharpSyntaxTree.ParseText(text, ParseOptions, filePath), token);
            }

            for (int i = 0; i < projectCount; i++)
            {
                var listOfTasks = new List<Task<SyntaxTree>>();
                syntaxTreeTasksLists[i] = listOfTasks;
                var directoryPath = projectDatas.Projects[i].DirectoryFullPath;
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
            for (int i = 0; i < Administrators.Count; i++)
            {
                var text = Administrators[i].GetAnnotations();
                var task = StartParseTask(text, filePath: null, CancellationToken);
                annotationSyntaxTreeTasks[i] = task;
            }
        }

        SyntaxTree[][] syntaxTreesArrays = new SyntaxTree[projectCount][];
        SyntaxTree[] annotationSyntaxTrees = new SyntaxTree[projectCount];   
        {
            for (int syntaxTreeListIndex = 0; syntaxTreeListIndex < projectCount; syntaxTreeListIndex++)
            {
                var syntaxTreeTasksList = syntaxTreeTasksLists[syntaxTreeListIndex];
                var length = syntaxTreeTasksList.Count;
                var syntaxTrees = new SyntaxTree[length];
                syntaxTreesArrays[syntaxTreeListIndex] = syntaxTrees;
                for (int i = 0; i < length; i++)
                    syntaxTrees[i] = await syntaxTreeTasksList[i];
            }

            {
                for (int adminIndex = 0; adminIndex < projectCount; adminIndex++)
                    annotationSyntaxTrees[adminIndex] = await annotationSyntaxTreeTasks[adminIndex];
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


        var compilation = CSharpCompilation.Create("Kari", 
            syntaxTreesArrays.SelectMany(t => t).Concat(annotationSyntaxTrees), metadata, CompilationOptions);

        var symbolCollectionTasks = new Task<INamedTypeSymbol[]>[projectCount];
        {
            for (int projectIndex = 0; projectIndex < projectCount; projectIndex++)
            {
                var trees = syntaxTreesArrays[projectIndex];
                symbolCollectionTasks[projectIndex] = Task.Run(() => Collect(trees, compilation));
                
                static async Task<INamedTypeSymbol[]> Collect(SyntaxTree[] syntaxTrees, Compilation compilation)
                {
                    var result = new HashSet<INamedTypeSymbol>();
                    foreach (var syntaxTree in syntaxTrees)
                    {
                        var root = await syntaxTree.GetRootAsync();
                        var model = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
                        foreach (var node in root.DescendantNodes())
                        {
                            if (node is not TypeDeclarationSyntax tds)
                                continue;

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

        // var collectedSymbols = new INamedTypeSymbol[projectCount][];
        // for (int projectIndex = 0; projectIndex < projectCount; projectIndex++)
        //     collectedSymbols[projectIndex] = await symbolCollectionTasks[projectIndex];

        
        // Sets up Projects and PseudoProjects
        {
            var projects = new ProjectEnvironment[projectCount];
            for (int projectIndex = 0; projectIndex < projectCount; projectIndex++)
            {
                projects[projectIndex] = new ProjectEnvironment(
                    projectDatas.Projects[projectIndex],
                    syntaxTreesArrays[projectIndex],
                    annotationSyntaxTrees[projectIndex],
                    await symbolCollectionTasks[projectIndex]); 
            }

            // Here we finally assign the things.
            this.Projects = projects;
            (this.RootPseudoProject, this.RootPseudoProjectIndex) = projectDatas.Root;
            (this.CommonPseudoProject, this.CommonPseudoProjectIndex) = projectDatas.Common;
        }

        this.Compilation = compilation;
        Symbols.Initialize(compilation);
    }

    public void InitializeAdministrators()
    {
        foreach (var admin in Administrators)
        {
            admin.Initialize();
        }
    }

    public async Task CollectSymbols()
    {
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
            var outputDirectory = Path.Join(project.DirectoryFullPath, generatedFolderRelativePath);
            CodeFileCommon.InitializeGeneratedDirectory(outputDirectory);
            var fragments = CollectionsMarshal.AsSpan(project.CodeFragments);
            var fileNamesInfo = GeneratedFileNamesInfo.Create(fragments, Logger);

            return new SingleDirectoryOutputResult(
                new GeneratedPathsInfo(outputDirectory, fileNamesInfo), 
                WriteCodeFragmentsToSeparateFiles(
                    outputDirectory, fragments, fileNamesInfo.FileNames, CancellationToken));
        }

        return AllProjectDatas.Select(ProcessProject).ToArray();
    }

    /// <summary>
    /// This method assumes:
    /// 1. The project names correspond to namespaces. 
    /// 2. The project names are unique.
    /// </summary>
    public SingleDirectoryOutputResult[] WriteCodeFiles_CentralDirectory(string generatedFolderFullPath)
    {
        SingleDirectoryOutputResult ProcessProject(ProjectEnvironmentData project)
        {
            var outputDirectory = Path.Join(generatedFolderFullPath, project.Name);
            CodeFileCommon.InitializeGeneratedDirectory(outputDirectory);
            var fragments = CollectionsMarshal.AsSpan(project.CodeFragments);
            var fileNamesInfo = GeneratedFileNamesInfo.Create(fragments, Logger);

            return new SingleDirectoryOutputResult(
                new GeneratedPathsInfo(outputDirectory, fileNamesInfo),
                WriteCodeFragmentsToSeparateFiles(
                    outputDirectory, fragments, fileNamesInfo.FileNames, CancellationToken));
        }

        return AllProjectDatas.Select(ProcessProject).ToArray();
    }

    public void DisposeOfAllCodeFragments()
    {
        foreach (var p in AllProjectDatas)
            p.DisposeOfCodeFragments();
    }

    internal static IEnumerable<ArraySegment<byte>> GetProjectArraySegmentsForSingleFileOutput(ProjectEnvironmentData project)
    {
        yield return CodeFileCommon.SlashesSpaceBytes;
        yield return Encoding.UTF8.GetBytes(project.Name);
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

        foreach (var project in AllProjectDatas)
        {
            project.CodeFragments.Sort();
        }

        return WriteArraySegmentsToCodeFileAsync(
            singleOutputFileFullPath, 
            WrapArraySegmentsWithHeaderAndFooter(
                AllProjectDatas.SelectMany(GetProjectArraySegmentsForSingleFileOutput)), 
            CancellationToken);
    }

    public Task WriteCodeFiles_SingleNestedFile(string singleOutputFileRelativeToProjectDirectoryPath)
    {
        Task ProcessProject(ProjectEnvironmentData project)
        {
            project.CodeFragments.Sort();
            string outputFilePath = Path.Join(project.DirectoryFullPath, singleOutputFileRelativeToProjectDirectoryPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));
            return WriteArraySegmentsToCodeFileAsync(
                outputFilePath,
                WrapArraySegmentsWithHeaderAndFooter(GetProjectArraySegmentsForSingleFileOutput(project)),
                CancellationToken);
        }

        return Task.WhenAll(AllProjectDatas.Select(ProcessProject));
    }
}
