
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kari.GeneratorCore.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kari.GeneratorCore
{
    public class CodeGenerator
    {
        private static readonly Encoding NoBomUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly System.Action<string> logger;
        private readonly CancellationToken cancellationToken;

        public CodeGenerator(System.Action<string> logger, CancellationToken cancellationToken)
        {
            this.logger = logger;
            this.cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Generates the specialized resolver and formatters for the types that require serialization in a given compilation.
        /// </summary>
        /// <param name="compilation">The compilation to read types from as an input to code generation.</param>
        /// <param name="outputDirectoryOrFile">The name of the generated source file.</param>
        /// <param name="namespace">The namespace for the generated type to be created in. May be null.</param>
        /// <returns>A task that indicates when generation has completed.</returns>
        public async Task GenerateFileAsync(
           Compilation compilation,
           string rootNamespace,
           string outputDirectoryOrFile,
           bool clearOutputDirectory,
           string[] pluginsPaths)
        {
            
            // =======================================================================
            var sw = new Stopwatch(); 
            
            void Measure(string name, System.Action action)
            {
                sw.Restart();
                logger($"{name} Start");
                Task.Run(action);
                logger($"{name} Complete: {sw.Elapsed.ToString()}");
            }
            
            var tokenSource = new CancellationTokenSource();
            var master = new MasterEnvironment("SomeProject", "SomeFolder", cancellationToken);

            Measure("Project Compilation", () => {
                AdministratorFinder.LoadPluginsPaths(pluginsPaths);
                AdministratorFinder.AddAllAdministrators(master);
                master.InitializeCompilation("idk", ref compilation);
                master.FindProjects();
                master.InitializeAdministrators();
            });

            Measure("Method Collect", async () => {
                await master.Collect();
                master.RunCallbacks();
            });

            Measure("Output Generation", async () => await master.GenerateCode());
        }
    }
}
