namespace Kari.Generator
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;
    using Kari.GeneratorCore;
    using Kari.GeneratorCore.Workflow;
    using Microsoft.Build.Locator;
    using Microsoft.Build.Logging;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.MSBuild;

    public class KariCompiler
    {
        [Option("Show help for Kari and all specified plugins. Call with no arguments to show help just for Kari",
            IsFlag = true)]
        bool help;

        [Option("Input path to MSBuild project file or to the directory containing source files.", 
            IsRequired = true)] 
        string input;

        [Option("Plugins folder or full paths to individual plugin dlls.",
            IsRequired = true)]
        string[] pluginsLocations;

        [Option("The suffix added to each subproject (or the root project) indicating the output folder.")] 
        string generatedName = "Generated";

        [Option("Conditional compiler symbols. Ignored if a project file is specified for input.")] 
        string[] conditionalSymbols = null;

        [Option("Set input namespace root name.")]
        string rootNamespace = "";

        [Option("Delete all cs files in the output folder.", 
            IsFlag = true)]
        bool clearOutput = false;

        [Option("Plugin names to be used for code analysis and generation. All plugins are used by default.")]
        string[] pluginNames = null;

        [Option("Whether to output all code into a single file.",
            IsFlag = true)]
        bool singleFileOutput = false;

        [Option("Whether to not scan for subprojects and always treat the entire codebase as a single root project. This implies the files will be generated in a single folder. With `singleFileOutput` set to true implies generating all code for the entire project in the single file.",
            IsFlag = true)]
        bool monolithicProject = false;

        [Option("The common project namespace name (use $Root to mean the root namespace). This is the project where all the attributes and other things common to all projects will end up. Ignored when `monolithicProject` is set to true.")]
        string commonNamespace = "$Root.Common";

        [Option("The subnamespaces ignored for the particular project, but which are treated as a separate project, even if they sit in the same root namespace.")]
        HashSet<string> independentNamespaceParts = new HashSet<string> { "Editor", "Tests" };

        [Option("Whether to treat 'Editor' folders as separate subprojects, even if they contain no asmdef. Only the editor folder that is at root of a folder with asmdef is regarded this way, nested Editor folders are ignored.")]
        bool treatEditorAsSubproject = true;


        public enum ExitCode
        {
            Ok = 0,
            OptionSyntaxError = 1,
            BadOptionValue = 2,
            UnmatchedArguments = 3,
            Other = 4,
            OperationCanceled = 5
        }
        
        private static async Task<int> Main(string[] args)
        {
            var instance = MSBuildLocator.RegisterDefaults();
            AssemblyLoadContext.Default.Resolving += (assemblyLoadContext, assemblyName) =>
            {
                var path = Path.Combine(instance.MSBuildPath, assemblyName.Name + ".dll");
                Console.WriteLine(path);
                if (File.Exists(path))
                {
                    return assemblyLoadContext.LoadFromAssemblyPath(path);
                }

                return null;
            };

            var tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            ArgumentParser parser = new ArgumentParser();
            var result = parser.ParseArguments(args);

            if (result.IsError)
            {
                System.Console.Error.WriteLine(result.Error);
                return (int) ExitCode.OptionSyntaxError;
            }

            var compiler = new KariCompiler();

            if (parser.IsEmpty)
            {
                System.Console.WriteLine(parser.GetHelpFor(compiler));
                return 0;
            }

            try
            {
                System.Environment.ExitCode = await compiler.RunAsync(parser, token);
            }
            catch (OperationCanceledException)
            {
                return (int) ExitCode.OperationCanceled;
            }
            finally
            {
                tokenSource.Dispose();
            }

            return 0;
        }


        private Logger _logger;

        private void PreprocessOptions(ArgumentParser parser)
        {
            var result = parser.FillObjectWithOptionValues(this);
            if (result.IsError)
            {
                foreach (var e in result.Errors)
                {
                    _logger.LogError(e);
                }
                return;
            }

            input            = Path.GetFullPath(NormalizeDirectorySeparators(input));
            pluginsLocations = pluginsLocations?.Select(s => NormalizeDirectorySeparators(s)).ToArray();
            generatedName    = NormalizeDirectorySeparators(generatedName);

            if (generatedName == "" && clearOutput)
            {
                _logger.LogNoLock($"Setting the `generatedName` to an empty string and `clearOutputFolder` to true will wipe all top-level source files in your project. (In principle! I WON'T do that.) Specify a different folder or file.", LogType.Error);
            }

            // Validate the independent namespaces + initialize.
            foreach (var part in independentNamespaceParts)
            {
                if (!SyntaxFacts.IsValidIdentifier(part))
                {
                    _logger.LogError($"Independent namespace part {part} does not represent a valid identifier.");
                }
            }

            var namespacePrefixRoot = string.IsNullOrEmpty(rootNamespace) 
                ? "" : rootNamespace + '.';

            commonNamespace = commonNamespace.Replace("$Root.", namespacePrefixRoot);
        }

        private async Task<int> RunAsync(ArgumentParser parser, CancellationToken token)
        {
            Workspace workspace = null;
            try // I hate that I have to do the stupid try-catch with awaits, 
                // but at least I don't necessarily need to indent it.
            {

            bool ShouldExit()
            {
                return Logger.AnyLoggerHasErrors || token.IsCancellationRequested;
            }

            int Exit(ExitCode code)
            {
                workspace?.Dispose();
                return (int) code;
            }

            var entireFunctionTimer = Stopwatch.StartNew();
            _logger = new Logger("Master");

            PreprocessOptions(parser);
            if (ShouldExit())  return (int) ExitCode.BadOptionValue;

            // Input must be either a directory of source files or an msbuild project
            string projectDirectory;
            bool isProjectADirectory;
            if (Directory.Exists(input))
            {
                projectDirectory = input;
                isProjectADirectory = true;
            }
            else if (File.Exists(input))
            {
                projectDirectory = Path.GetDirectoryName(input);
                isProjectADirectory = false;
            }
            else
            {
                _logger.LogError($"No such input file or directory {input}.");
                return 1;
            }
            if (ShouldExit()) return Exit(ExitCode.BadOptionValue);

            // We need to initialize this in order to create Administrators.
            // Even if help is set, we need to new them up in order to retrieve the help messages,
            // so creating this is a prerequisite.
            var master = new MasterEnvironment(rootNamespace, projectDirectory, token, _logger, independentNamespaceParts);
            // Set the master instance globally
            MasterEnvironment.InitializeSingleton(master);
            master.GeneratedPath = generatedName;

            // This means no subprojects
            if (monolithicProject)
            {
                master.CommonProjectName = null;
            }
            else if (commonNamespace == "")
            {
                _logger.LogError($"The common project name cannot be empty. If you wish to treat the entire code base as one monolithic project, use that option instead.");
            }
            else
            {
                master.CommonProjectName = commonNamespace;
            }


            Task pluginsTask = _logger.MeasureAsync("Load Plugins", () => LoadPlugins(master, token));

            if (help)
            {
                // Wait until the dll's have loaded and the Administrator have been set
                await pluginsTask;

                Logger.LogPlain("Use Kari to generate code for a C# project.\nMain Options:");
                Logger.LogPlain(parser.GetHelpFor(this));
                master.LogHelpForEachAdministrator(parser);
                return Exit(ExitCode.Ok);
            }

            // Now finally create the compilation
            Task compileTask;
            Compilation compilation = null;
            if (isProjectADirectory)
            {
                compileTask = _logger.MeasureAsync("Pseudo Compilation", () =>
                    compilation = PseudoCompilation.CreateFromDirectory(input, generatedName, token));
            }
            else
            {
                compileTask = _logger.MeasureAsync("MSBuild Compilation", () =>
                    (workspace, compilation) = OpenMSBuildProjectAsync(input, token).Result);
            }
            
            var outputPath = generatedName;
            if (!Path.IsPathFullyQualified(generatedName))
            {
                master.GeneratedNamespaceSuffix = generatedName;
                outputPath = Path.Combine(projectDirectory, generatedName);
            }
            
            if (singleFileOutput)
            {
                if (!Path.GetExtension(generatedName).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError($"If the option {nameof(singleFileOutput)} is specified, the generated path must be a relative to a file with the '.cs' extension, got {generatedName}.");
                }
                else
                {
                    master.RootWriter = new OneFilePerProjectFileWriter(outputPath);
                }
            }
            else
            {
                master.RootWriter = new SeparateCodeFileWriter(outputPath);
            }

            await pluginsTask;

            // Now that plugins have loaded, we can actually use the rest of the arguments.
            master.TakeCommandLineArguments(parser);
            if (ShouldExit()) return Exit(ExitCode.BadOptionValue);

            var unrecognizedOptions = parser.GetUnrecognizedOptions();
            if (unrecognizedOptions.Any())
            {
                foreach (var arg in unrecognizedOptions)
                {
                    _logger.LogError($"Unrecognized option: `{arg}`");
                }
                Exit(ExitCode.Other);
            }

            await compileTask;
            if (ShouldExit()) return Exit(ExitCode.Other);

            // TODO: This code looks ugly af actually. But manual Start() End() Print() are even uglier.
            // TODO: Is this profiling thing even useful? I mean, we already get the stats for the whole function. 
            _logger.MeasureSync("Environment Initialization", () => {
                master.InitializeCompilation(ref compilation);
                if (!monolithicProject) master.FindProjects(treatEditorAsSubproject);
                master.InitializePseudoProjects();
                master.InitializeAdministrators();
            });
            if (ShouldExit()) return Exit(ExitCode.Other);

            async Task startCollectTask() {
                await master.Collect();
                master.RunCallbacks();
            }
            await _logger.MeasureAsync("Symbol Collect", startCollectTask());
            if (ShouldExit()) return Exit(ExitCode.Other);

            async Task startGenerateTask()
            {
                if (clearOutput) master.ClearOutput();
                await master.GenerateCode();
                master.CloseWriters();
            }
            await _logger.MeasureAsync("Output Generation", startGenerateTask());
            if (ShouldExit()) return Exit(ExitCode.Other);
            
            _logger.LogInfo("Generation complete. It took " + entireFunctionTimer.Elapsed.ToString());
            return Exit(ExitCode.Ok);


            } // try
            catch (OperationCanceledException)
            {
                return (int) ExitCode.OperationCanceled;
            }
            finally
            {
                workspace?.Dispose();
            }
        }

        private void LoadPlugins(MasterEnvironment master, CancellationToken cancellationToken)
        {
            var finder = new AdministratorFinder();

            for (int i = 0; i < pluginsLocations.Length; i++)
            {
                void Handle(string error) 
                {
                    _logger.LogError($"Error while processing plugin input #{i}, {pluginsLocations[i]}: {error}");
                }

                try
                {
                    pluginsLocations[i] = Path.GetFullPath(pluginsLocations[i]);
                    if (Directory.Exists(pluginsLocations[i]))
                    {
                        finder.LoadPluginsDirectory(pluginsLocations[i]);
                        continue;
                    }
                    if (File.Exists(pluginsLocations[i]))
                    {
                        finder.LoadPlugin(pluginsLocations[i]);
                        continue;
                    }
                    Handle("The specified plugin folder or file does not exist.");
                    if (cancellationToken.IsCancellationRequested) return;
                }
                catch (FileLoadException exception)
                {
                    Handle(exception.Message);
                }
                catch (BadImageFormatException exception)
                {
                    Handle(exception.Message);
                }
            }

            if (Logger.AnyLoggerHasErrors) return;

            if (pluginNames is null)
            {
                finder.AddAllAdministrators(master);
            }
            else
            {
                var names = pluginNames.ToHashSet();
                finder.AddAdministrators(master, names);

                if (names.Count > 0)
                {
                    foreach (var name in names)
                    {
                        _logger.LogError($"Invalid administrator name: {name}");
                    }
                }
            }
        }

        private async Task<(Workspace, Compilation)> OpenMSBuildProjectAsync(string projectPath, CancellationToken cancellationToken)
        {
            var workspace = MSBuildWorkspace.Create();
            try
            {
                var logger = new ConsoleLogger(Microsoft.Build.Framework.LoggerVerbosity.Quiet);
                var project = await workspace.OpenProjectAsync(projectPath, logger, null, cancellationToken);
                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation is null)
                {
                    _logger.LogError($"The input project {projectPath} does not support compilation.");
                }

                return (workspace, compilation);
            }
            catch
            {
                workspace.Dispose();
                throw;
            }
        }
        

        private static string NormalizeDirectorySeparators(string path)
        {
            return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
