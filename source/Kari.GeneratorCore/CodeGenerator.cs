// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kari.GeneratorCore.CodeAnalysis;
using Kari.Generator;
using Kari.GeneratorCore.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kari.GeneratorCore
{
    public class CodeGenerator
    {
        private static readonly Encoding NoBomUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private Action<string> logger;
        private CancellationToken cancellationToken;

        public CodeGenerator(Action<string> logger, CancellationToken cancellationToken)
        {
            this.logger = logger;
            this.cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Generates the specialized resolver and formatters for the types that require serialization in a given compilation.
        /// </summary>
        /// <param name="compilation">The compilation to read types from as an input to code generation.</param>
        /// <param name="output">The name of the generated source file.</param>
        /// <param name="namespace">The namespace for the generated type to be created in. May be null.</param>
        /// <returns>A task that indicates when generation has completed.</returns>
        public async Task GenerateFileAsync(
           Compilation compilation,
           string output,
           string @namespace)
        {
            var namespaceDot = string.IsNullOrWhiteSpace(@namespace) ? string.Empty : @namespace + ".";
            bool hadAnnotations = compilation.ContainsSymbolsWithName(nameof(Kari.Shared.WeirdDetectionAttribute));

            // Perhaps not the most ideal check, but I'm sure it will work out.
            if (!hadAnnotations)
            {
                compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(DummyAttributes.Text));
            }
            
            var sw = Stopwatch.StartNew();
            logger("Project Compilation Start:" + compilation.AssemblyName);
            // var collector = new TypeCollector(compilation, true, useMapMode, externalIgnoreTypeNames, x => Console.WriteLine(x));
            logger("Project Compilation Complete:" + sw.Elapsed.ToString());

            logger("Method Collect Start");
            sw.Restart();
            // var (objectInfo, enumInfo, genericInfo, unionInfo) = collector.Collect();
            logger("Method Collect Complete:" + sw.Elapsed.ToString());

            logger("Output Generation Start");
            sw.Restart();
            if (Path.GetExtension(output) == ".cs")
            {
                // Single-file output
                var t = new TestTemplate { Namespace = @namespace + "Generated" };

                var sb = new StringBuilder();
                sb.AppendLine(t.TransformText());
                sb.AppendLine();

                if (!hadAnnotations)
                {
                    sb.AppendLine(DummyAttributes.Text);
                }

                await OutputAsync(output, sb.ToString(), cancellationToken);
            }
            else
            {
                // Multiple-file output
                var t = new TestTemplate { Namespace = @namespace + "Generated" };
                await OutputToDirAsync(output, t.Namespace, "TestName.cs", t.TransformText(), cancellationToken);

                if (!hadAnnotations)
                {
                    await OutputAsync(Path.Combine(output, "/Annotations.cs"), DummyAttributes.Text, cancellationToken);
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
            return content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }
    }
}
