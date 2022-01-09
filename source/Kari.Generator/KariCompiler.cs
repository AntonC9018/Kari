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
    using Microsoft.CodeAnalysis;
    using static System.Diagnostics.Debug;

    public class KariCompiler
    {
        public class KariOptions
        {
            public string HelpMessage => "Use Kari to generate code for a C# project.";

            [Option("Input path to the directory containing source files or projects.", 
                IsPath = true)] 
            public string inputFolder = ".";

            [Option("Plugins folder or paths to individual plugin dlls.",
                // Can be sometimes inferred from input, aka NuGet's packages.config
                IsRequired = false,
                IsPath = true)]
            public string[] pluginPaths = null;

            [Option("Path to `packages.config` that you're using to manage packages. The plugins mentioned in that file will be imported.",
                IsPath = true)]
            public string pluginConfigFilePath = null;

            [Option("The suffix added to the project namespace to generate the output namespace.")]
            public string generatedNamespaceSuffix = "Generated";

            [Option("Conditional compiler symbols. Ignored if a project file is specified for input. (Currently ignored)")] 
            public string[] conditionalSymbols = null;

            [Option("Set input namespace root name.")]
            public string rootNamespace = "";

            [Option("Plugin names to be used for code analysis and generation. All plugins are used by default.")]
            public string[] pluginNames = null;

            [Option("The code by default will be generated in a nested folder with this name. If `centralInput` is true, this indicates the central output folder path, relative to `input`. If `singleFileOutput` is set to true, '.cs' may be appended to this name to indicate the output file name.")] 
            public string generatedName = "Generated";

            [Option("Where to place the generated files.")]
            public MasterEnvironment.OutputMode outputMode = MasterEnvironment.OutputMode.NestedDirectory;

            [Option(@"`UnityAsmdefs` means it will search for asmdefs.
`MSBuild` and `ByDirectory` are equivalent: they assume the given path is a root folder, where each subfolder is a separate project. Nested projects are currently not allowed.
`Monolithic` there are source files to be analysed in the root folder, as well as in nested folders.
`Autodetect` means that the input will be selected by looking at the file system's entries. If there are asmdefs, Unity will be guessed, if there are source files in root, Monolithic. At last, it will default to `ByDirectory` if none of the above were true.")]
            public MasterEnvironment.InputMode inputMode = MasterEnvironment.InputMode.Autodetect;

            [Option("The name of the common project. Corresponds either to the directory name (`ByDirectory`) or the project file name (`Unity`). Leave at default to let it autodetect.")]
            public string commonProjectName = "";

            [Option("The name of the root project. Corresponds either to the directory name (`ByDirectory`) or the project file name (`Unity`). By default, it would generate in the root directory. You can either pass a name of one of the projects that you want to be root here, or just give it the folder name relative to the root directory and it would generate into that folder.")]
            public string rootProjectName = "";

            [Option("The directories, source files in which will be ignored. The generated source files are always ignored.")]
            public List<string> ignoredNames = new List<string> { "obj", "bin" };

            [Option("The full directory or file paths which will be ignored when reading source files. The generated source files are always ignored.")]
            public List<string> ignoredFullPaths = new List<string> {};

            [Option("Which projects to generate code for. (Unimplemented)")]
            public HashSet<string> whitelistGeneratedCodeForProjects = null;

            [Option("Which projects to read the code of. (Unimplemented)")]
            public HashSet<string> whitelistAnalyzedProjects = null;
            
            [Option("Paths to assemblies to load annotations from. The `object` assembly is always loaded. (Unimplemented)", 
                IsPath = true)]
            public string[] additionalAnnotationAssemblyPaths = null;

            [Option("Names of assemblies to load annotations from. These will be searched in the default location. Use `additionalAnnotationAssemblyPaths` to straight up load from paths. (Unimplemented)")] 
            public string[] additionalAnnotationAssemblyNames = null;
        }
        private KariOptions _ops;
        private NamedLogger _logger;

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
            NoPlugins = 10,
        }
        
        private static async Task<int> Main(string[] args)
        {
            var instance = MSBuildLocator.RegisterDefaults();
            AssemblyLoadContext.Default.Resolving += (assemblyLoadContext, assemblyName) =>
            {
                var path = Path.Join(instance.MSBuildPath, assemblyName.Name + ".dll");
                Console.WriteLine(path);
                if (File.Exists(path))
                {
                    return assemblyLoadContext.LoadFromAssemblyPath(path);
                }

                return null;
            };

            var tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            var argumentLogger = new NamedLogger("Arguments");

            ArgumentParser parser = new ArgumentParser();
            var result = parser.ParseArguments(args);
            if (result.IsError)
            {
                argumentLogger.LogError(result.Error);
                return (int) ExitCode.OptionSyntaxError;
            }
            
            if (parser.IsVersionSet)
            {
                NamedLogger.LogPlain($"{ThisAssembly.AssemblyName} version {ThisAssembly.AssemblyInformationalVersion}.");
                return 0;
            }

            result = parser.MaybeParseConfiguration("kari");
            if (result.IsError)
            {
                argumentLogger.LogError(result.Error);
                return (int) ExitCode.OptionSyntaxError;
            }

            var ops = new KariOptions();
            var result1 = parser.FillObjectWithOptionValues(ops);

            if (result1.IsError)
            {
                foreach (var e in result1.Errors)
                    argumentLogger.LogError(e);
                return (int) ExitCode.BadOptionValue;
            }

            var compiler = new KariCompiler();
            compiler._ops = ops;
            compiler._logger = new NamedLogger("Master");

            if (parser.IsEmpty)
            {
                parser.LogHelpFor(compiler._ops);
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

        private async Task<ExitCode> RunAsync(ArgumentParser parser, CancellationToken token)
        {
            Workspace workspace = null;
            try // I hate that I have to do the stupid try-catch with awaits, 
                // but at least I don't necessarily need to indent it.
            {

            bool ShouldExit()
            {
                return NamedLogger.AnyLoggerHasErrors || token.IsCancellationRequested;
            }

            string validOutputPath = "";
            
            // Option preprocessing and validation.
            {
                if (_ops.pluginConfigFilePath is not null)
                    _ops.pluginConfigFilePath = Path.GetFullPath(_ops.pluginConfigFilePath);

                // Since there is a default value, this may actually still not be a full path at this point.
                if (!Path.IsPathRooted(_ops.inputFolder))
                    _ops.inputFolder = Path.GetFullPath(_ops.inputFolder);

                if (!parser.IsHelpSet)
                {
                    {
                        _ops.generatedName = _ops.generatedName.WithNormalizedDirectorySeparators();
                        if (_ops.generatedName == "")
                        {
                            _logger.LogError($"Setting the `generatedName` to an empty string will wipe all top-level source files in your project. (In principle! I WON'T do that.) Specify a different folder or file.");
                        }
                    }

                    // {
                    //     var namespacePrefixRoot = string.IsNullOrEmpty(_ops.rootNamespace) 
                    //         ? "" : _ops.rootNamespace + '.';

                    //     _ops.commonProjectName = _ops.commonProjectName.Replace("$Root.", namespacePrefixRoot);
                    // }

                    if (_ops.pluginConfigFilePath is not null && !File.Exists(_ops.pluginConfigFilePath))
                    {
                        _logger.LogError($"Plugin config file {_ops.pluginConfigFilePath} does not exist.");
                    }

                    if (!Directory.Exists(_ops.inputFolder))
                    {
                        _logger.LogError($"No such input directory {_ops.inputFolder}.");
                    }

                    // Preprocess and validate the output path.
                    {
                        validOutputPath = _ops.generatedName.WithNormalizedDirectorySeparators();
                        if (_ops.outputMode.HasFlag(MasterEnvironment.OutputMode.Central))
                        {
                            if (!Path.IsPathRooted(validOutputPath))
                            {
                                validOutputPath = Path.GetFullPath(Path.Join(_ops.inputFolder, validOutputPath));
                            }
                            _ops.ignoredFullPaths.Add(validOutputPath);
                        }
                        else
                        {
                            if (Path.IsPathRooted(validOutputPath))
                            {
                                _logger.LogError("When the output mode is not set to central, the path cannot be absolute.");
                            }
                            else
                            {
                                _ops.ignoredNames.Add(validOutputPath);
                            }
                        }
                        if (_ops.outputMode.HasFlag(MasterEnvironment.OutputMode.File))
                        {
                            if (!validOutputPath.EndsWith(".cs"))
                                validOutputPath += ".cs";
                        }
                    }
                }
                else
                {
                    if (_ops.pluginPaths is null && _ops.pluginConfigFilePath is null)
                    {
                        parser.LogHelpFor(_ops);
                        return ExitCode.Ok;
                    }
                }

                if (ShouldExit())
                    return ExitCode.BadOptionValue;
            }

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
                        packageDirectoryName = pluginDirectoryNames
                            .Where(d => d.StartsWith(id))
                            .OrderBy(d => d, StringComparer.Ordinal)
                            .FirstOrDefault();

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

                    var pluginRootFullPath = Path.Join(pluginConfigDirectory, packageDirectoryName);
                    // We've checked the enumerable above, so this should be true.
                    Assert(Directory.Exists(pluginRootFullPath));

                    string libPath = Path.Join(pluginRootFullPath, "lib", targetFramework);
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

            
            var pluginPaths = Enumerable.Concat(GetDllPathsFromUserGivenPaths(), GetNugetPluginPaths());
            if (!pluginPaths.Any())
            {
                _logger.LogError("Kari won't generate any code if it were given no plugins.");
                return ExitCode.NoPlugins;
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

                parser.LogHelpFor(_ops);
                master.LogHelpForEachAdministrator(parser);
                return ExitCode.Ok;
            }

            await pluginsTask;

            // Now that plugins have loaded, we can finally use the rest of the arguments.
            {
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

                if (ShouldExit())
                    return ExitCode.Other;
            }

            var entireGenerationMeasurer = new Measurer(_logger);
            entireGenerationMeasurer.Start("The entire generation process");

            // TODO: Is this profiling thing even useful? I mean, we already get the stats for the whole function. 
            var measurer = new Measurer(_logger);
            measurer.Start("Environment Initialization");
            {
                var projectNamesInfo = new ProjectNamesInfo
                {
                    RootNamespaceName        = _ops.rootNamespace,
                    RootPseudoProjectName    = _ops.rootProjectName,
                    CommonPseudoProjectName  = _ops.commonProjectName,
                    GeneratedNamespaceSuffix = _ops.generatedNamespaceSuffix,
                    ProjectRootDirectory     = _ops.inputFolder,
                    IgnoredFullPaths         = _ops.ignoredFullPaths,
                    IgnoredNames             = _ops.ignoredNames,
                };
                
                await master.InitializeCompilation(projectNamesInfo, _ops.inputMode);

                if (ShouldExit())
                    return ExitCode.FailedEnvironmentInitialization;
                
                // Set the master instance globally.
                // It is only needed as a convenience for plugins.
                MasterEnvironment.InitializeSingleton(master);

                master.InitializeAdministrators();

                if (ShouldExit())
                    return ExitCode.FailedEnvironmentInitialization;
            }
            measurer.Stop();

            measurer.Start("Symbol Collect");
            {
                await master.CollectSymbols();
                
                if (ShouldExit())
                    return ExitCode.FailedSymbolCollection;
            }
            measurer.Stop();

            measurer.Start("Output Generation");
            {
                await master.GenerateCodeFragments();

                switch (_ops.outputMode)
                {
                    case MasterEnvironment.OutputMode.NestedFile:
                    {
                        await master.WriteCodeFiles_SingleNestedFile(validOutputPath);
                        break;
                    }

                    case MasterEnvironment.OutputMode.CentralFile:
                    {
                        await master.WriteCodeFiles_CentralFile(validOutputPath);
                        break;
                    }

                    case MasterEnvironment.OutputMode.NestedDirectory:
                    {
                        foreach (var a in master.WriteCodeFiles_NestedDirectory(validOutputPath))
                        {
                            a.GeneratedPaths.RemoveCodeFilesThatWereNotGenerated();
                            await a.WriteOutputTask;
                        }
                        break;
                    }

                    case MasterEnvironment.OutputMode.CentralDirectory:
                    {
                        foreach (var a in master.WriteCodeFiles_CentralDirectory(validOutputPath))
                        {
                            a.GeneratedPaths.RemoveCodeFilesThatWereNotGenerated();
                            await a.WriteOutputTask;
                        }
                        break;
                    }
                    
                    default: 
                    {
                        Assert(false); 
                        break;
                    }
                }

                master.DisposeOfAllCodeFragments();

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
    }
}
