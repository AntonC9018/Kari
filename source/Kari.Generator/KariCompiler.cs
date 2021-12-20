namespace Kari.Generator
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;
    using System.Xml.Linq;
    using Kari.Arguments;
    using Kari.GeneratorCore.Workflow;
    using Kari.Utils;
    using Microsoft.Build.Locator;
    using Microsoft.Build.Logging;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.MSBuild;
    using static System.Diagnostics.Debug;

    public class KariCompiler
    {
        string HelpMessage => "Use Kari to generate code for a C# project.";

        [Option("Input path to MSBuild project file or to the directory containing source files.")] 
        string input = ".";

        [Option("Plugins folder or paths to individual plugin dlls.",
            // Can be sometimes inferred from input, aka NuGet's packages.config
            IsRequired = false)]
        string[] pluginPaths = null;

        [Option("Path to `packages.config` that you're using to manage packages. The plugins mentioned in that file will be imported.")]
        string pluginConfigFilePath = null;

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
            input = Path.GetFullPath(input.WithNormalizedDirectorySeparators());
            generatedName = generatedName.WithNormalizedDirectorySeparators();
            if (pluginConfigFilePath is not null)
                pluginConfigFilePath = Path.GetFullPath(pluginConfigFilePath);

            // Hacky?
            if (parser.IsHelpSet)
                return;

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

            if (parser.IsHelpSet && pluginPaths is null && pluginConfigFilePath is null)
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
                if (!File.Exists(pluginConfigFilePath))
                {
                    _logger.LogError($"Plugin config file {pluginConfigFilePath} does not exist");
                }
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
                }
                if (ShouldExit())
                    return ExitCode.BadOptionValue;
            }

            // We need to initialize this in order to create Administrators.
            // Even if help is set, we need to new them up in order to retrieve the help messages,
            // so creating this is a prerequisite.
            var master = new MasterEnvironment(rootNamespace, projectDirectory, token, _logger, independentNamespaceParts);

            IEnumerable<string> GetNugetPluginPaths()
            {
                if (pluginConfigFilePath is null)
                    yield break;

                var pluginConfigDirectory = Path.GetDirectoryName(pluginConfigFilePath);
                var pluginConfigXml = XDocument.Load(pluginConfigFilePath, LoadOptions.SetLineInfo);
                var packages = pluginConfigXml.Root;
                if (packages.Name != "packages")
                {
                    _logger.LogError("The root element must be named `packages`");
                    yield break;
                }

                var pluginDirectoryNames = Directory.EnumerateDirectories(pluginConfigDirectory)
                    // Actually gets just the last directory name (I think this function returns no slashes??)
                    .Select(d => Path.GetFileName(d));

                foreach (var package in packages.Elements())
                {
                    // I'm not sure non-package elements are allowed tho
                    if (package.Name != "package")
                        continue;

                    string getLocation()
                    { 
                        IXmlLineInfo info = package; 
                        return $"{pluginConfigFilePath}({info.LineNumber},{info.LinePosition})"; 
                    }

                    var attributes = package.Attributes();
                    var idAttribute = attributes.Where(a => a.Name == "id").FirstOrDefault();
                    if (idAttribute is null)
                    {
                        _logger.LogError($"{getLocation()}: Wrong format: you forgot to specify the id for the package");
                        continue;
                    }
                    var id = idAttribute.Value;

                    var targetFrameworkAttribute = attributes.Where(a => a.Name == "targetFramework").FirstOrDefault();
                    var targetFramework = targetFrameworkAttribute?.Value ?? "net5.0";

                    // We return the directories, since my function loads from directories fine anyway.
                    string packageDirectoryName;

                    var versionAttribute = attributes.Where(a => a.Name == "version").FirstOrDefault();
                    // no version is fine, we just do the default one in this case
                    if (versionAttribute is null)
                    {
                        packageDirectoryName = pluginDirectoryNames.FirstOrDefault(d => d.StartsWith(id));
                        if (packageDirectoryName is null)
                        {
                            _logger.LogError($"Not found a directory for the plugin `{id}` at {getLocation()}. (Did you forget to restore?)");
                            continue;
                        }
                    }
                    else
                    {
                        var version = versionAttribute.Value;
                        packageDirectoryName = $"{id}.{version}";

                        // I'm not sure which one should be preferred?
                        // if (!Directory.Exists(packageDirName))
                        if (!pluginDirectoryNames.Contains(packageDirectoryName))
                        {
                            _logger.LogError($"Expected to find the directory {packageDirectoryName} within the plugin folder, but didn't. (Did you forget to restore?)");
                            continue;
                        }
                    }

                    var pluginRootFullPath = Path.Combine(pluginConfigDirectory, packageDirectoryName);
                    // We've checked the enumerable above, so this should be true.
                    Assert(Directory.Exists(pluginRootFullPath));

                    string libPath = Path.Combine(pluginRootFullPath, "lib", targetFramework);
                    if (!Directory.Exists(libPath))
                    {
                        _logger.LogError($"The plugin {packageDirectoryName} had no lib folder, so it's probably not even a plugin, someone messed something up.");
                        continue;
                    }
                    yield return libPath;
                }
            }

            // It is fine to not have master entirely globally initialized at this point.
            // The plugin loading only needs the administrator list.
            Task pluginsTask = _logger.MeasureAsync("Load Plugins", 
                delegate { LoadPlugins(pluginPaths.Concat(GetNugetPluginPaths()), master, token); });
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
            if (ShouldExit())
                return ExitCode.BadOptionValue;

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

        private void LoadPlugins(IEnumerable<string> pluginPaths, MasterEnvironment master, CancellationToken cancellationToken)
        {
            var finder = new AdministratorFinder();

            foreach (var pluginPath in pluginPaths)
            {
                void Handle(string error) 
                {
                    _logger.LogError($"Error while processing plugin input {pluginPath}: {error}");
                }

                try
                {
                    var pluginFullPath = Path.GetFullPath(pluginPath);
                    if (Directory.Exists(pluginFullPath))
                    {
                        finder.LoadPluginsDirectory(pluginFullPath);
                        continue;
                    }
                    if (File.Exists(pluginFullPath))
                    {
                        finder.LoadPlugin(pluginFullPath);
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

            // We don't return here, since I still want to load all available plugins for help
            // if (Logger.AnyLoggerHasErrors)
            //     return;

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
