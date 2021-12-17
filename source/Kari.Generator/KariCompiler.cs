namespace Kari.Generator
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;
    using Kari.Arguments;
    using Kari.GeneratorCore.Workflow;
    using Kari.Utils;
    using Microsoft.Build.Locator;
    using Microsoft.Build.Logging;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.MSBuild;

    public class KariCompiler
    {
        string HelpMessage => "Use Kari to generate code for a C# project.";

        [Option("Input path to MSBuild project file or to the directory containing source files.")] 
        string input = ".";

        [Option("Plugins folder or paths to individual plugin dlls.",
            IsRequired = true)]
        string[] pluginPaths;

        [Option("The suffix added to each subproject (or the root project) indicating the output folder.")] 
        string generatedName = "Generated";

        [Option("Conditional compiler symbols. Ignored if a project file is specified for input. (Currently ignored)")] 
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
            OperationCanceled = 5,
            UnknownOptions = 6,
            FailedEnvironmentInitialization = 7,
            FailedSymbolCollection = 8,
            FailedOutputGeneration = 9,
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

            Logger argumentLogger = new Logger("Arguments");

            ArgumentParser parser = new ArgumentParser();
            var result = parser.ParseArguments(args);
            if (result.IsError)
            {
                argumentLogger.LogError(result.Error);
                return (int) ExitCode.OptionSyntaxError;
            }

            result = parser.MaybeParseConfiguration("kari");
            if (result.IsError)
            {
                argumentLogger.LogError(result.Error);
                return (int) ExitCode.OptionSyntaxError;
            }

            var compiler = new KariCompiler();

            if (parser.IsEmpty)
            {
                argumentLogger.Log(parser.GetHelpFor(compiler), LogType.Information);
                return 0;
            }

            try
            {
                System.Environment.ExitCode = (int) await compiler.RunAsync(parser, token);
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

            pluginPaths = pluginPaths?.Select(FileSystem.WithNormalizedDirectorySeparators).ToArray();

            // Hacky?
            if (parser.IsHelpSet)
                return;

            input = Path.GetFullPath(input.WithNormalizedDirectorySeparators());
            generatedName = generatedName.WithNormalizedDirectorySeparators();

            if (generatedName == "" && clearOutput)
            {
                _logger.LogError($"Setting the `generatedName` to an empty string and `clearOutputFolder` to true will wipe all top-level source files in your project. (In principle! I WON'T do that.) Specify a different folder or file.");
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

        private async Task<ExitCode> RunAsync(ArgumentParser parser, CancellationToken token)
        {
            Workspace workspace = null;
            try // I hate that I have to do the stupid try-catch with awaits, 
                // but at least I don't necessarily need to indent it.
            {

            bool ShouldExit()
            {
                return Logger.AnyLoggerHasErrors || token.IsCancellationRequested;
            }

            _logger = new Logger("Master");
            var entireGenerationMeasurer = new Measurer(_logger);
            entireGenerationMeasurer.Start("The entire generation process");

            PreprocessOptions(parser);

            if (parser.IsHelpSet && pluginPaths is null)
            {
                Logger.LogPlain(parser.GetHelpFor(this));
                return ExitCode.Ok;
            }
            if (ShouldExit())
                return ExitCode.BadOptionValue;

            // Input must be either a directory of source files or an msbuild project
            string projectDirectory = "";
            bool isProjectADirectory = false;
            if (!parser.IsHelpSet)
            {
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
                    return ExitCode.Other;
                }
                if (ShouldExit())
                    return ExitCode.BadOptionValue;
            }

            // We need to initialize this in order to create Administrators.
            // Even if help is set, we need to new them up in order to retrieve the help messages,
            // so creating this is a prerequisite.
            var master = new MasterEnvironment(rootNamespace, projectDirectory, token, _logger, independentNamespaceParts);
            
            // It is fine to not have master entirely globally initialized.
            // The plugin loading only needs the administrator list.
            Task pluginsTask = _logger.MeasureAsync("Load Plugins", () => LoadPlugins(master, token));
            if (parser.IsHelpSet)
            {
                await pluginsTask;

                Logger.LogPlain(parser.GetHelpFor(this));
                master.LogHelpForEachAdministrator(parser);
                return ExitCode.Ok;
            }

            // Set the master instance globally
            MasterEnvironment.InitializeSingleton(master);

            master.GeneratedPath = generatedName;
            // This means no subprojects
            if (monolithicProject)
            {
                master.CommonProjectNamespaceName = null;
            }
            else if (commonNamespace == "")
            {
                _logger.LogError($"The common project name cannot be empty. If you wish to treat the entire code base as one monolithic project, use that option instead.");
            }
            else
            {
                master.CommonProjectNamespaceName = commonNamespace;
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
            if (ShouldExit()) return ExitCode.BadOptionValue;

            var unrecognizedOptions = parser.GetUnrecognizedOptions();
            var unrecognizedConfigOptions = parser.GetUnrecognizedOptionsFromConfigurations();
            if (unrecognizedOptions.Any() || unrecognizedConfigOptions.Any())
            {
                foreach (var arg in unrecognizedOptions)
                {
                    _logger.LogError($"Unrecognized option: `{arg}`");
                }
                foreach (var arg in unrecognizedConfigOptions)
                {
                    // TODO: This can contain more info, like the line number.
                    _logger.LogError($"Unrecognized option: `{arg.GetPropertyPath()}`");
                }
                return ExitCode.UnknownOptions;
            }

            await compileTask;
            if (ShouldExit())
                return ExitCode.Other;

            // The code is a bit less ugly now, but still pretty ugly.
            // TODO: Is this profiling thing even useful? I mean, we already get the stats for the whole function. 
            var measurer = new Measurer(_logger);

            measurer.Start("Environment Initialization");
            {
                master.InitializeCompilation(ref compilation);
                if (ShouldExit())
                    return ExitCode.FailedEnvironmentInitialization;
                if (!monolithicProject) 
                    master.FindProjects(treatEditorAsSubproject);
                master.InitializePseudoProjects();
                master.InitializeAdministrators();
            }
            if (ShouldExit())
                return ExitCode.FailedEnvironmentInitialization;
            measurer.Stop();

            measurer.Start("Symbol Collect");
            {
                await master.Collect();
                master.RunCallbacks();
            }
            if (ShouldExit())
                return ExitCode.FailedSymbolCollection;
            measurer.Stop();

            measurer.Start("Output Generation");
            {
                if (clearOutput) master.ClearOutput();
                await master.GenerateCode();
                master.CloseWriters();
            }
            if (ShouldExit())
                return ExitCode.FailedOutputGeneration;
            
            entireGenerationMeasurer.Stop();
            return ExitCode.Ok;


            } // try
            catch (OperationCanceledException)
            {
                return ExitCode.OperationCanceled;
            }
            finally
            {
                workspace?.Dispose();
            }
        }

        private void LoadPlugins(MasterEnvironment master, CancellationToken cancellationToken)
        {
            var finder = new AdministratorFinder();

            for (int i = 0; i < pluginPaths.Length; i++)
            {
                void Handle(string error) 
                {
                    _logger.LogError($"Error while processing plugin input #{i}, {pluginPaths[i]}: {error}");
                }

                try
                {
                    pluginPaths[i] = Path.GetFullPath(pluginPaths[i]);
                    if (Directory.Exists(pluginPaths[i]))
                    {
                        finder.LoadPluginsDirectory(pluginPaths[i]);
                        continue;
                    }
                    if (File.Exists(pluginPaths[i]))
                    {
                        finder.LoadPlugin(pluginPaths[i]);
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

            if (Logger.AnyLoggerHasErrors)
                return;

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
    }
}
