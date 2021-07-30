
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
           bool writeAttributes,
           bool clearOutputDirectory)
        {
            var namespaceDot = string.IsNullOrWhiteSpace(outNamespace) ? string.Empty : outNamespace + ".";
            bool hadAnnotations = compilation.ContainsSymbolsWithName(nameof(Kari.KariWeirdDetectionAttribute));

            // Perhaps not the most ideal check, but I'm sure it will work out.
            if (!hadAnnotations)
            {
                compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(DummyAttributes.Text));
            }
            
            // =======================================================================
            var sw = Stopwatch.StartNew(); logger("Project Compilation Start: " + compilation.AssemblyName);

            var environment = new Environment(compilation, rootNamespace, logger);
            var parsersTemplate   = new ParsersTemplate();
            var commandsTemplate  = new CommandsTemplate();
            var flagsTemplate     = new FlagsTemplate();

            flagsTemplate.Namespace = outNamespace;

            // TODO: Both the output folder and the namespace should be configurable per project (e.g. asmdef)
            commandsTemplate.Namespace = rootNamespace + ".CommandTerminal.Generated";
            parsersTemplate.Namespace = rootNamespace + ".CommandTerminal.Generated";

            logger("Project Compilation Complete:" + sw.Elapsed.ToString());

            // =======================================================================
            logger("Method Collect Start"); sw.Restart();
            environment.Collect();
            parsersTemplate.CollectInfo(environment);
            commandsTemplate.CollectInfo(environment, parsersTemplate);
            flagsTemplate.CollectInfo(environment);
            logger("Method Collect Complete:" + sw.Elapsed.ToString());

            // =======================================================================
            logger("Output Generation Start"); sw.Restart();
            if (Path.GetExtension(outputDirectoryOrFile) != ".cs")
            {
                var outputDirectory = outputDirectoryOrFile;
                if (Directory.Exists(outputDirectory))
                {
                    if (clearOutputDirectory)
                    {
                        foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.cs"))
                        {
                            File.Delete(file);
                        }
                    }
                }
                else
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                // Multiple-file output
                await OutputToDirAsync(outputDirectoryOrFile, commandsTemplate.Namespace, "Commands", commandsTemplate.TransformText(), cancellationToken);
                await OutputToDirAsync(outputDirectoryOrFile, "Kari.CommandTerminal", "Parsers", parsersTemplate.TransformText(), cancellationToken);
                await OutputToDirAsync(outputDirectoryOrFile, flagsTemplate.Namespace, "Flags", flagsTemplate.TransformText(), cancellationToken);

                if (writeAttributes && !hadAnnotations)
                {
                    await OutputAsync(Path.Combine(outputDirectoryOrFile, "Annotations.cs"), DummyAttributes.Text, cancellationToken);
                }
            }
            logger("Output Generation Complete:" + sw.Elapsed.ToString());
        }

        private Task OutputToDirAsync(string dir, string ns, string name, string text, CancellationToken cancellationToken)
        {
            return OutputAsync(Path.Combine(dir, $"{ns}_{name}".Replace(".", "_").Replace("global::", string.Empty) + ".cs"), text, cancellationToken);
        }

        private Task OutputAsync(string path, string text, CancellationToken cancellationToken)
        {
            path = path.Replace("global::", string.Empty);

            const string prefix = "[Out]";
            logger(prefix + path);

            var fi = new FileInfo(path);
            if (!fi.Directory.Exists)
            {
                fi.Directory.Create();
            }

            System.IO.File.WriteAllText(path, NormalizeNewLines(text), NoBomUtf8);
            return Task.CompletedTask;
        }

        private static string NormalizeNewLines(string content)
        {
            // The T4 generated code may be text with mixed line ending types. (CR + CRLF)
            // We need to normalize the line ending type in each Operating Systems. (e.g. Windows=CRLF, Linux/macOS=LF)
            return content.Replace("\r\n", "\n").Replace("\n", System.Environment.NewLine);
        }
    }
}
