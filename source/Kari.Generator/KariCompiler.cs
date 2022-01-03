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
        public class KariOptions
        {
            public string HelpMessage => "Use Kari to generate code for a C# project.";

            [Option("Input path to MSBuild project file or to the directory containing source files.", 
                IsPath = true)] 
            public string input = ".";

            [Option("Plugins folder or paths to individual plugin dlls.",
                // Can be sometimes inferred from input, aka NuGet's packages.config
                IsRequired = false,
                IsPath = true)]
            public string[] pluginPaths = null;

            [Option("Path to `packages.config` that you're using to manage packages. The plugins mentioned in that file will be imported.",
                IsPath = true)]
            public string pluginConfigFilePath = null;

            [Option("The suffix added to each subproject (or the root project) indicating the output folder.")] 
            public string generatedName = "Generated";

            [Option("Conditional compiler symbols. Ignored if a project file is specified for input. (Currently ignored)")] 
            public string[] conditionalSymbols = null;

            [Option("Set input namespace root name.")]
            public string rootNamespace = "";

            [Option("Plugin names to be used for code analysis and generation. All plugins are used by default.")]
            public string[] pluginNames = null;

            [Option("Whether to output all code into a single file.",
                IsFlag = true)]
            public bool singleFileOutput = false;

            [Option("Whether to not scan for subprojects and always treat the entire codebase as a single root project. This implies the files will be generated in a single folder. With `singleFileOutput` set to true implies generating all code for the entire project in the single file.",
                IsFlag = true)]
            public bool monolithicProject = false;

            [Option("The common project namespace name (use $Root to mean the root namespace). This is the project where all the attributes and other things common to all projects will end up. Ignored when `monolithicProject` is set to true.")]
            public string commonNamespace = "$Root.Common";

            [Option("The subnamespaces ignored for the particular project, but which are treated as a separate project, even if they sit in the same root namespace.")]
            public HashSet<string> independentNamespaceParts = new HashSet<string> { "Editor", "Tests" };

            [Option("Whether to treat 'Editor' folders as separate subprojects, even if they contain no asmdef. Only the editor folder that is at root of a folder with asmdef is regarded this way, nested Editor folders are ignored.")]
            public bool treatEditorAsSubproject = true;
        }
        private KariOptions _ops;
        private Logger _logger;

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
            compiler._ops = new KariOptions();
            compiler._logger = new Logger("Master");

            if (parser.IsEmpty)
            {
                argumentLogger.Log(parser.GetHelpFor(compiler._ops), LogType.Information);
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

            _ops.generatedName = _ops.generatedName.WithNormalizedDirectorySeparators();

            if (_ops.pluginConfigFilePath is not null)
                _ops.pluginConfigFilePath = Path.GetFullPath(_ops.pluginConfigFilePath);

            // Hacky?
            if (parser.IsHelpSet)
                return;

            if (_ops.generatedName == "")
            {
                _logger.LogError($"Setting the `generatedName` to an empty string will wipe all top-level source files in your project. (In principle! I WON'T do that.) Specify a different folder or file.");
            }

            // Validate the independent namespaces + initialize.
            foreach (var part in _ops.independentNamespaceParts)
            {
                if (!SyntaxFacts.IsValidIdentifier(part))
                {
                    _logger.LogError($"Independent namespace part {part} does not represent a valid identifier.");
                }
            }

            var namespacePrefixRoot = string.IsNullOrEmpty(_ops.rootNamespace) 
                ? "" : _ops.rootNamespace + '.';

            _ops.commonNamespace = _ops.commonNamespace.Replace("$Root.", namespacePrefixRoot);
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

            var entireGenerationMeasurer = new Measurer(_logger);
            entireGenerationMeasurer.Start("The entire generation process");

            PreprocessOptions(parser);

            if (parser.IsHelpSet && _ops.pluginPaths is null && _ops.pluginConfigFilePath is null)
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
                if (_ops.pluginConfigFilePath is not null && !File.Exists(_ops.pluginConfigFilePath))
                {
                    _logger.LogError($"Plugin config file {_ops.pluginConfigFilePath} does not exist");
                }
                if (Directory.Exists(_ops.input))
                {
                    projectDirectory = _ops.input;
                    isProjectADirectory = true;
                }
                else if (File.Exists(_ops.input))
                {
                    projectDirectory = Path.GetDirectoryName(_ops.input);
                    isProjectADirectory = false;
                }
                else
                {
                    _logger.LogError($"No such input file or directory {_ops.input}.");
                }
                if (ShouldExit())
                    return ExitCode.BadOptionValue;
            }
            
            // This means no subprojects
            if (!_ops.monolithicProject && _ops.commonNamespace == "")
            {
                _logger.LogError($"The common project name cannot be empty. If you wish to treat the entire code base as one monolithic project, use that option instead.");
            }

            var projectNamesInfo = new ProjectNamesInfo
            {
                RootNamespaceName = _ops.rootNamespace,
                CommonProjectNamespaceName = _ops.monolithicProject ? null : _ops.commonNamespace,
                GeneratedNamespaceSuffix = "Generated", // TODO
                ProjectRootDirectory = projectDirectory
            };

            // We need to initialize this in order to create Administrators.
            // Even if help is set, we need to new them up in order to retrieve the help messages,
            // so creating this is a prerequisite.
            var master = new MasterEnvironment(token, _logger);

            IEnumerable<string> GetNugetPluginPaths()
            {
                if (_ops.pluginConfigFilePath is null)
                    yield break;

                var pluginConfigDirectory = Path.GetDirectoryName(_ops.pluginConfigFilePath);
                var pluginConfigXml = XDocument.Load(_ops.pluginConfigFilePath, LoadOptions.SetLineInfo);
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
                        return $"{_ops.pluginConfigFilePath}({info.LineNumber},{info.LinePosition})"; 
                    }

                    var attributes = package.Attributes();
                    var idAttribute = attributes.Where(a => a.Name == "id").FirstOrDefault();
                    if (idAttribute is null)
                    {
                        _logger.LogError($"{getLocation()}: Wrong format: you forgot to specify the id for the package.");
                        continue;
                    }
                    var id = idAttribute.Value;

                    var targetFrameworkAttribute = attributes.Where(a => a.Name == "targetFramework").FirstOrDefault();
                    var targetFramework = targetFrameworkAttribute?.Value ?? "net6.0";

                    string packageDirectoryName;

                    var versionAttribute = attributes.Where(a => a.Name == "version").FirstOrDefault();
                    // No version is fine, we just do the default one in this case.
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
                            _logger.LogError($"Could not find the directory `{packageDirectoryName}` within the plugin folder. (Did you forget to restore?)");
                            continue;
                        }
                    }

                    var pluginRootFullPath = Path.Combine(pluginConfigDirectory, packageDirectoryName);
                    // We've checked the enumerable above, so this should be true.
                    Assert(Directory.Exists(pluginRootFullPath));

                    string libPath = Path.Combine(pluginRootFullPath, "lib", targetFramework);
                    if (!Directory.Exists(libPath))
                    {
                        _logger.LogError($"The plugin `{packageDirectoryName}` had no lib folder, so it's probably not even a plugin, someone messed something up.");
                        continue;
                    }
                    foreach (var dllFullPath in Directory.EnumerateFiles(libPath, "*.dll", SearchOption.AllDirectories))
                        yield return dllFullPath;
                }
            }

            IEnumerable<string> GetDllPathsFromUserGivenPaths()
            {
                if (_ops.pluginPaths is null)
                    yield break;

                // These paths are already prenormalized by the argument parser
                foreach (var p in _ops.pluginPaths)
                {
                    if (File.Exists(p))
                    {
                        if (p.EndsWith("dll", StringComparison.OrdinalIgnoreCase))
                        {
                            yield return p;
                        }
                        else
                        {
                            _logger.LogError($"Plugin file `{p}` is not a dynamic library.");
                        }
                    }
                    else if (Directory.Exists(p))
                    {
                        foreach (var dllPath in Directory.EnumerateFiles(p, "*.dll", SearchOption.AllDirectories))
                        {
                            yield return dllPath;
                        }
                    }
                    else
                    {
                        _logger.LogError($"Plugin file or directory `{p}` did not exist.");
                    }
                }
            }

            // It is fine to not have master entirely globally initialized at this point.
            // The plugin loading only needs the administrator list.
            Task pluginsTask = _logger.MeasureAsync("Load Plugins", 
                delegate 
                {
                    var paths = Enumerable.Concat(GetDllPathsFromUserGivenPaths(), GetNugetPluginPaths());
                    LoadPlugins(paths, master, token); 
                });

            if (parser.IsHelpSet)
            {
                await pluginsTask;

                Logger.LogPlain(parser.GetHelpFor(this));
                master.LogHelpForEachAdministrator(parser);
                return ExitCode.Ok;
            }

            // Set the master instance globally
            MasterEnvironment.InitializeSingleton(master);


            // Now finally create the compilation
            Task compileTask;
            Compilation compilation = null;
            if (isProjectADirectory)
            {
                compileTask = _logger.MeasureAsync("Pseudo Compilation", () =>
                    compilation = PseudoCompilation.CreateFromDirectory(_ops.input, _ops.generatedName, token));
            }
            else
            {
                compileTask = _logger.MeasureAsync("MSBuild Compilation", () =>
                    (workspace, compilation) = OpenMSBuildProjectAsync(_ops.input, token).Result);
            }
            
            var outputPath = _ops.generatedName;
            if (!Path.IsPathFullyQualified(outputPath))
                outputPath = Path.Combine(projectDirectory, _ops.generatedName);
            
            if (_ops.singleFileOutput)
            {
                if (!Path.GetExtension(_ops.generatedName).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError($"If the option `{nameof(_ops.singleFileOutput)}` is set, the generated path must be relative to a file with the '.cs' extension, got {_ops.generatedName}.");
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
                    _logger.LogError($"Unrecognized option: `{parser.GetPropertyPathOfOption(arg)}`");
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
                master.InitializeCompilation(ref compilation, projectNamesInfo.RootNamespaceName);
            measurer.Stop();

            measurer.Start("Environment Initialization2");
            {
                if (ShouldExit())
                    return ExitCode.FailedEnvironmentInitialization;
                if (!_ops.monolithicProject) 
                    master.FindProjects(projectNamesInfo, _ops.treatEditorAsSubproject);
                master.InitializePseudoProjects(projectNamesInfo);
                master.InitializeAdministrators();

                if (ShouldExit())
                    return ExitCode.FailedEnvironmentInitialization;
            }
            measurer.Stop();

            measurer.Start("Symbol Collect");
            {
                await master.Collect(_ops.independentNamespaceParts);
                
                if (ShouldExit())
                    return ExitCode.FailedSymbolCollection;
            }
            measurer.Stop();

            measurer.Start("Output Generation");
            {
                await master.GenerateCode();
                await master.WriteCodeFiles_SplitByProject(_ops.generatedName);

                if (ShouldExit())
                    return ExitCode.FailedOutputGeneration;
            }
            measurer.Stop();

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

        private void LoadPlugins(IEnumerable<string> pluginDllPaths, MasterEnvironment inoutMaster, CancellationToken cancellationToken)
        {
            foreach (var dllPath in pluginDllPaths)
            {
                void Handle(string error) 
                {
                    _logger.LogError($"Error while processing plugin input {dllPath}: {error}");
                }

                try
                {
                    AdministratorFinder.LoadPlugin(dllPath);
                    if (cancellationToken.IsCancellationRequested) 
                        return;
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

            if (_ops.pluginNames is null)
            {
                AdministratorFinder.AddAllAdministrators(inoutMaster);
            }
            else
            {
                var names = _ops.pluginNames.ToHashSet();
                AdministratorFinder.AddAdministrators(inoutMaster, names);

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
