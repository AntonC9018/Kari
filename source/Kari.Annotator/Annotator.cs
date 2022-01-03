
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kari.Arguments;
using Kari.Utils;
using static System.Diagnostics.Debug;

namespace Kari.Annotator
{
    /// The point of this app is to be able to find all annotation files,
    /// and generate helper files for them in the plugins.
    public class Annotator
    {
        [Option("Specific files to target. By default, all files ending with Annotations.cs are targeted (recursively)")]
        string[] targetedFiles = null;
        [Option("The folder relative to which search the files.")]
        string targetedFolder = ".";
        [Option("The regex string used to find target files.")]
        string targetFileRegex = @".*(Annotations|Attributes)$";
        [Option("Suffix to append to generated files. E.g. Annotations.cs -> Annotations.Generated.cs")]
        string generatedFileSuffix = ".Generated";
        [Option("Regex pattern that tells whether a directory should be ignored")]
        string ignoredDirectoriesPattern = "obj|bin";
        [Option("An absolute path or a path relative to `targetedFolder` of the directory in which to output the generated files. By default, the files get output next to the source files.")]
        string generatedFilesOutputFolder = null;
        [Option(" ")]
        string singleFileOutputName = null;
        [Option("Whether to not replace all instances of internal with public in the source file",
            IsFlag = true)]
        bool noReplaceInternalWithPublic = false;
        // TODO: see if the files changed (cache the timestamps of when the dependent files changed last).
        // [Option(" ", IsFlag = true)]
        // bool clearOutputFolder = false;
        [Option("The namespace that will replace the original namespace in the client version of the file. By default, the namespace stays as is.")]
        string clientNamespaceSubstitute = null;
        [Option("The namespace that will replace the original namespace in the plugin helper file. By default, the namespace stays as is.")]
        string pluginNamespaceSubstitute = null;
        [Option("public / internal")]
        string classVisibility = "internal";


        internal static int Main(string[] args)
        {
            var argumentLogger = new Logger("Arguments");
            var parser = new ArgumentParser();
            var result = parser.ParseArguments(args);
            if (result.IsError)
            {
                argumentLogger.LogError(result.Error);
                return 1;
            }

            result = parser.MaybeParseConfiguration("annotator");
            if (result.IsError)
            {
                argumentLogger.LogError(result.Error);
                return 1;
            }

            var annotator = new Annotator();
            if (parser.IsHelpSet)
            {
                argumentLogger.Log(parser.GetHelpFor(annotator), LogType.Information);
                return 0;
            }

            var result2 = parser.FillObjectWithOptionValues(annotator);
            if (result2.IsError)
            {
                foreach (var error in result2.Errors)
                    argumentLogger.LogError(error);
                return 1;
            }

            return annotator.Run();
        }

        private class ShouldIgnoreDirectory : FileSystem.IShouldIgnoreDirectory
        {
            public Regex pattern;
            public string fullIgnoredPath;

            bool FileSystem.IShouldIgnoreDirectory.ShouldIgnoreDirectory(string fullFilePath)
            {
                return fullFilePath == fullIgnoredPath || pattern.IsMatch(fullFilePath);
            }
        }

        public int Run()
        {
            Logger logger = new Logger("Annotator");
            targetedFolder = targetedFolder.WithNormalizedDirectorySeparators();
            generatedFilesOutputFolder = generatedFilesOutputFolder?.WithNormalizedDirectorySeparators();
            singleFileOutputName = singleFileOutputName?.WithNormalizedDirectorySeparators();

            // TODO: more error handling won't hurt
            if (generatedFileSuffix == "")
            {
                if (generatedFilesOutputFolder is null)
                {
                    logger.LogError("Both the suffix and the generated subfolder were empty. That means the newly generated files cannot be differentiated from the source files (for sure an error).");
                    return 1;
                }
            }

            var targetRegex = new Regex(targetFileRegex, RegexOptions.IgnoreCase);
            if (targetedFiles is null)
            {
                var ignoreMetric = new ShouldIgnoreDirectory();
                try
                {
                    ignoreMetric.pattern = new Regex(ignoredDirectoriesPattern);
                }
                catch (RegexParseException except)
                {
                    logger.LogError($"The regex {ignoredDirectoriesPattern} failed:\n{except}.");
                    return 1;
                }

                try
                {

                    if (generatedFilesOutputFolder is null 
                        // Non-empty suffix should in theory imply the generated files won't match the
                        // regex that determines files to be processed.
                        || generatedFileSuffix != "")
                    {
                        // sourceFiles = Directory.EnumerateFiles(targetedFolder, "*.cs", SearchOption.AllDirectories);
                    }
                    else // if (generatedFilesOutputFolder is not null)
                    {
                        generatedFilesOutputFolder = Path.GetFullPath(generatedFilesOutputFolder);
                        if (!Directory.Exists(generatedFilesOutputFolder))
                            Directory.CreateDirectory(generatedFilesOutputFolder);
                        ignoreMetric.fullIgnoredPath = generatedFilesOutputFolder;
                    }

                    // We'll be checking out all files, so compiling the regex should be useful? it depends.
                    targetedFiles = FileSystem.EnumerateFilesIgnoring(targetedFolder, ignoreMetric, "*.cs")
                        .Where(f => targetRegex.IsMatch(Path.GetFileNameWithoutExtension(f)))
                        .ToArray();
                }
                catch (RegexParseException except)
                {
                    logger.LogError($"The regex {targetFileRegex} failed:\n{except}.");
                    return 1;
                }
            }

            // THOUGHT: I probably should just use the parser here
            const string qualifiedIdentifierRegexString = @"([a-zA-Z_][a-zA-Z0-9_]*\.)*([a-zA-Z_][a-zA-Z0-9_]*)";
            var namespaceRegex = new Regex(@"namespace\s+(?<namespace>" + qualifiedIdentifierRegexString + @")\s*{");
            var attributeClassRegex = new Regex(@"class\s+(?<attribute>[a-zA-Z_][a-zA-Z0-9_]*Attribute)\s*:\s*" 
                + qualifiedIdentifierRegexString + @"?Attribute");

            CodeBuilder builder = CodeBuilder.Create();

            string GetOutputFilePath(string inputFilePath)
            {
                var generatedFilePath = Path.ChangeExtension(inputFilePath, generatedFileSuffix + ".cs");
                if (generatedFilesOutputFolder is not null)
                    return Path.Join(generatedFilesOutputFolder, Path.GetFileName(generatedFilePath));
                return generatedFilePath;
            }

            foreach (var inputFilePath in targetedFiles)
            {
                // MSBuild checks timestamps instead of content too, I'm pretty sure.
                // This is probably why it rebuilt my plugins, thinking the content changed.
                var inputInfo      = new FileInfo(inputFilePath);
                var outputFilePath = GetOutputFilePath(inputFilePath);
                var outputInfo     = new FileInfo(outputFilePath);
                if (inputInfo.LastWriteTime <= outputInfo.LastWriteTime)
                    continue;

                string attributesText = File.ReadAllText(inputFilePath, Encoding.UTF8);
                string attributesTextEscaped = attributesText.Replace("\"", "\"\"");
                if (!noReplaceInternalWithPublic)
                    attributesTextEscaped = attributesTextEscaped.Replace("internal", "public");

                var namespaceDeclaration = namespaceRegex.Match(attributesTextEscaped);

                builder.Indent();
                builder.Append("namespace ");
                if (pluginNamespaceSubstitute is not null)
                    builder.Append(pluginNamespaceSubstitute);
                else
                    builder.Append(namespaceDeclaration.Groups["namespace"].Value);
                builder.NewLine();
                builder.StartBlock();

                string classname = Path.GetFileNameWithoutExtension(inputFilePath);
                builder.AppendLine("using Kari.GeneratorCore.Workflow;");
                builder.AppendLine("using Kari.Utils;");
                builder.AppendLine($"{classVisibility} static class Dummy{classname}");
                builder.StartBlock();

                builder.Indent();
                builder.Append($"{classVisibility} const string Text = @\"");
                if (clientNamespaceSubstitute is not null)
                {
                    builder.Append(attributesTextEscaped.AsSpan(0, namespaceDeclaration.Index));
                    builder.Append(clientNamespaceSubstitute);
                    int continueIndex = namespaceDeclaration.Index + namespaceDeclaration.Length;
                    builder.Append(attributesTextEscaped.AsSpan(continueIndex, attributesTextEscaped.Length - continueIndex));
                }
                else
                {
                    builder.Append(attributesTextEscaped);
                }
                builder.Append("\";");
                builder.NewLine();
                builder.EndBlock();

                // Attributes -> Symbols
                string GetSymbolsClassName()
                {
                    var targetRegexMatch = targetRegex.Match(classname);
                    if (targetRegexMatch.Success 
                        && targetRegexMatch.Groups.Count > 0)
                    {
                        var lastGroup = targetRegexMatch.Groups[^1];
                        return classname
                            .Remove(lastGroup.Index, lastGroup.Length)
                            .Insert(lastGroup.Index, "Symbols");
                    }
                    return classname;
                }
                builder.AppendLine($"{classVisibility} static partial class {GetSymbolsClassName()}");
                builder.StartBlock();

                var initializeBuilder = builder.NewWithPreservedIndentation();
                initializeBuilder.AppendLine(classVisibility, " static void Initialize(Logger logger)");
                initializeBuilder.StartBlock();
                initializeBuilder.AppendLine($"var compilation = MasterEnvironment.Instance.Compilation;");

                foreach (Match match in attributeClassRegex.Matches(attributesText))
                {
                    var attribute = match.Groups["attribute"];
                    Assert(attribute != null);
                    string type = $"AttributeSymbolWrapper<{attribute}>";
                    builder.AppendLine($"{classVisibility} static {type} {attribute} {{ get; private set; }}");
                    initializeBuilder.AppendLine($"{attribute} = new {type}(compilation, logger);");
                }

                initializeBuilder.EndBlock();
                
                builder.NewLine();
                builder.Append(ref initializeBuilder);

                // Apparently, one cannot both do `using` and pass as ref.
                // I say that makes no sense whatsoever.
                initializeBuilder.Dispose();

                builder.EndBlock();
                builder.EndBlock();

                if (singleFileOutputName is null)
                {
                    using (FileStream fs = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                        fs.Write(builder.AsArraySegment());
                    builder.Clear();
                }
            }

            if (singleFileOutputName is not null)
            {
                singleFileOutputName = Path.ChangeExtension(singleFileOutputName, ".cs");

                string GetPath()
                {
                    if (Path.IsPathRooted(singleFileOutputName))
                        return singleFileOutputName;
                    if (generatedFilesOutputFolder is not null)
                        return Path.Join(generatedFilesOutputFolder, singleFileOutputName);
                    return Path.Join(targetedFolder, singleFileOutputName);
                }

                string outputFilePath = GetPath();
                Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));
                File.WriteAllText(outputFilePath, outputFilePath);
            }
            else
            {
                Assert(builder.StringBuilder.Length == 0);
            }

            return 0;
        }
    }
}