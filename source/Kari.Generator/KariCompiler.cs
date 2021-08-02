namespace Kari.Generator
{
    using System;
    using System.Collections.Generic;
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
    using Microsoft.CodeAnalysis.CSharp;
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

        private void LoadPlugins(MasterEnvironment master, string pluginsLocations, string? namesToAdd)
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

            if (Logger.HasErrors) return;

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

        private Task<(Workspace? workspace, Compilation? compilation)> 
        CompileInput(string input, string generatedName)
        {
            

            return Task.FromResult<(Workspace? workspace, Compilation? compilation)>((null, null));
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
            bool clearOutputFolder = false,
            [Option("Plugin names to be used for code analysis and generation separated by ','. All plugins are used by default.")]
            string? pluginNames = null,
            [Option("Whether to output all code into a single file.")]
            bool singleFileOutput = false,
            [Option("The common project prefix. This is the project where all the attributes and things will end up. If single file output is set, they will end up in the single generated file.")]
            string commonName = "Common")
        {
            Workspace? workspace = null;
            try
            {
                _logger = new Logger("Master");
                if (generatedName == "" && clearOutputFolder)
                {
                    _logger.LogNoLock($"Setting the `generatedName` to an empty string and `clearOutputFolder` to true will wipe all top-level source files in your project. Specify a different folder or file.", LogType.Error);
                }

                Task compileTask;
                Compilation? compilation = null;

                string projectDirectory;

                if (Directory.Exists(input))
                {
                    projectDirectory = input;
                    compileTask = _logger.Measure("Pseudo Compilation", () =>
                        compilation = PseudoCompilation.CreateFromDirectory(input, generatedName));
                }
                else if (File.Exists(input))
                {
                    projectDirectory = Path.GetDirectoryName(input)!;
                    compileTask = _logger.Measure("MSBuild Compilation", () =>
                        (workspace, compilation) = OpenMSBuildProjectAsync(input, Context.CancellationToken).Result);
                }
                else
                {
                    _logger.LogError($"No such input file or directory {input}.");
                    return;
                }

                var master = new MasterEnvironment(rootNamespace, projectDirectory, Context.CancellationToken, _logger);
                // Set the master instance globally
                MasterEnvironment.InitializeSingleton(master);
                var pluginsTask = _logger.Measure("Load Plugins", () => LoadPlugins(master, pluginsLocations, pluginNames));
                
                await pluginsTask;
                await compileTask;
                if (Logger.HasErrors) { return; }

                
                IFileWriter writer;
                if (singleFileOutput)
                {
                    writer = new SingleCodeFileWriter();
                }
                else
                {
                    writer = new SeparateCodeFileWriter();
                }

                _logger.MeasureSync("Environment Initialization", () => {
                    master.InitializeCompilation(ref compilation);
                    if ()
                    master.FindProjects();
                    master.InitializeAdministrators();
                });

                await _logger.Measure("Method Collect", async () => {
                    await master.Collect();
                    master.RunCallbacks();
                });

                Measure("Output Generation", async () => await master.GenerateCode());
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
