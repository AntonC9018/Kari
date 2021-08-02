namespace Kari.Generator
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;
    using ConsoleAppFramework;
    using Kari.GeneratorCore.Workflow;
    using Microsoft.Build.Locator;
    using Microsoft.Build.Logging;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.MSBuild;
    using Microsoft.Extensions.Hosting;

    public class KariCompiler : ConsoleAppBase
    {
        private static async Task Main(string[] args)
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

            await Host.CreateDefaultBuilder()
                .ConfigureLogging(logging => logging.ReplaceToSimpleConsole())
                .RunConsoleAppFrameworkAsync<KariCompiler>(args);
        }

        private Logger _logger;

        private void LoadPlugins(MasterEnvironment master, string pluginsLocations, string? namesToAdd, CancellationToken cancellationToken)
        {
            var finder = new AdministratorFinder();
            string[] plugins = pluginsLocations.Split(',');

            for (int i = 0; i < plugins.Length; i++)
            {
                void Handle(string error) 
                {
                    _logger.LogError($"Error while processing plugin input #{i}, {plugins[i]}: {error}");
                }

                try
                {
                    if (Directory.Exists(plugins[i]))
                    {
                        finder.LoadPluginsDirectory(plugins[i]);
                        continue;
                    }
                    if (File.Exists(plugins[i]))
                    {
                        finder.LoadPlugin(plugins[i]);
                        continue;
                    }
                    Handle("The specified plugin folder or file does not exist.");
                    cancellationToken.ThrowIfCancellationRequested();
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

            if (namesToAdd is null)
            {
                finder.AddAllAdministrators(master);
            }
            else
            {
                var names = namesToAdd.Split(',').ToHashSet();
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

        public async Task RunAsync(
            [Option("Input path to MSBuild project file or to the directory containing source files.")] 
            string input,
            [Option("Plugins folder or full paths to individual plugin dlls separated by ','.")]
            string pluginsLocations,
            [Option("The suffix added to each subproject (or the root project) indicating the output folder.")] 
            string generatedName = "Generated",
            [Option("Conditional compiler symbols, split with ','. Ignored if a project file is specified for input.")] 
            string? conditionalSymbol = null,
            [Option("Set input namespace root name.")]
            string rootNamespace = "",
            [Option("Whether the attributes should be written to output.")]
            bool writeAttributes = true,
            [Option("Delete all cs files in the output folder.")]
            bool clearOutput = false,
            [Option("Plugin names to be used for code analysis and generation separated by ','. All plugins are used by default.")]
            string? pluginNames = null,
            [Option("Whether to output all code into a single file.")]
            bool singleFileOutput = false,
            [Option("Whether to not scan for subprojects and always treat the entire codebase as a single root project. This implies the files will be generated in a single folder. With `singleFileOutput` set to true implies generating all code for the entire project in the single file.")]
            bool monolithicProject = false,
            [Option("The common project namespace name (appended to rootNamespace). This is the project where all the attributes and other things common to all projects will end up. Ignored when `monolithicProject` is set to true.")]
            string commonName = "Common")
        {
            Workspace? workspace = null;
            try
            {
                _logger = new Logger("Master");
                if (generatedName == "" && clearOutput)
                {
                    _logger.LogNoLock($"Setting the `generatedName` to an empty string and `clearOutputFolder` to true will wipe all top-level source files in your project. (In principle! I WON'T do that.) Specify a different folder or file.", LogType.Error);
                }

                Task compileTask;
                Compilation? compilation = null;
                var token = Context.CancellationToken;

                string projectDirectory;

                if (Directory.Exists(input))
                {
                    projectDirectory = input;
                    compileTask = _logger.MeasureAsync("Pseudo Compilation", () =>
                        compilation = PseudoCompilation.CreateFromDirectory(input, generatedName, token));
                }
                else if (File.Exists(input))
                {
                    projectDirectory = Path.GetDirectoryName(input)!;
                    compileTask = _logger.MeasureAsync("MSBuild Compilation", () =>
                        (workspace, compilation) = OpenMSBuildProjectAsync(input, token).Result);
                }
                else
                {
                    _logger.LogError($"No such input file or directory {input}.");
                    return;
                }

                var master = new MasterEnvironment(rootNamespace, projectDirectory, token, _logger);
                // Set the master instance globally
                MasterEnvironment.InitializeSingleton(master);
                master.GeneratedPath = generatedName;

                if (monolithicProject)
                {
                    master.CommonProjectName = null;
                }
                else if (commonName == "")
                {
                    _logger.LogError($"The common project name cannot be empty. If you wish to treat the entire code base as one monolithic project, use that option instead.");
                }
                else
                {
                    master.CommonProjectName = rootNamespace.Combine(commonName);
                }

                var pluginsTask = _logger.MeasureAsync("Load Plugins", () => LoadPlugins(master, pluginsLocations, pluginNames, token));
                
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
                    master.RootWriter = new SeparateCodeFileWriter(generatedName);
                }

                await pluginsTask;
                await compileTask;
                if (Logger.AnyLoggerHasErrors) { return; }

                _logger.MeasureSync("Environment Initialization", () => {
                    master.InitializeCompilation(ref compilation);
                    if (!monolithicProject) master.FindProjects();
                    master.InitializePseudoProjects();
                    master.InitializeAdministrators();
                });
                if (Logger.AnyLoggerHasErrors) { return; }

                await _logger.MeasureAsync("Method Collect", async () => {
                    await master.Collect();
                    master.RunCallbacks();
                });
                if (Logger.AnyLoggerHasErrors) { return; }

                await _logger.MeasureAsync("Output Generation", async () => {
                    if (clearOutput) master.ClearOutput();
                    await master.GenerateCode();
                    master.CloseWriters();
                });
                if (Logger.AnyLoggerHasErrors) { return; }
            }
            catch (OperationCanceledException)
            {
                await Console.Error.WriteLineAsync("Canceled");
                throw;
            }
            finally
            {
                workspace?.Dispose();
            }
        }

        private async Task<(Workspace, Compilation?)> OpenMSBuildProjectAsync(string projectPath, CancellationToken cancellationToken)
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
