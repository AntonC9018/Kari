using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandDotNet;
using CommandDotNet.DataAnnotations;
using CommandDotNet.NameCasing;
using Kari.Utils;
using Microsoft.CodeAnalysis;
using static System.Diagnostics.Debug;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
// CommandDotNet requires optional arguments to be marked with the question mark.
#pragma warning disable CS8632

namespace Kari.Annotator
{
    /// The point of this app is to be able to find all annotation files,
    /// and generate helper files for them in the plugins.
    public class Annotator
    {
        public class Options : IArgumentModel
        {
            [Named(Description = "Specific files to target. By default, all files ending with Annotations.cs are targeted (recursively)")]
            public string[]? targetedFiles { get; set; } = null;
            
            [Named(Description = "The folder relative to which to search the files.")]
            public string targetedFolder { get; set; } = ".";
            
            [Named(Description = "The regex string used to find the target files.")]
            public string targetFileRegex { get; set; } = @".*(Annotations|Attributes)$";
            
            [Named(Description = "Suffix to append to the generated files. E.g. Annotations.cs -> Annotations.Generated.cs")]
            public string generatedFileSuffix  { get; set; } = ".Generated";
            
            [Named(Description = "Regex pattern that tells whether a directory should be ignored")]
            public string ignoredDirectoriesPattern { get; set; } = "obj|bin";
            
            [Named(Description = "An absolute path or a path relative to `targetedFolder` of the directory in which to output the generated files. By default, the files get output next to the source files.")]
            public string? generatedFilesOutputFolder { get; set; } = null;
            
            [Named]
            public string? singleFileOutputName { get; set; } = null;
            
            [Named(Description = "Whether to not replace all instances of internal with public in the source file",
                BooleanMode = BooleanMode.Implicit)]
            public bool noReplaceInternalWithPublic { get; set; } = false;
            
            // TODO: see if the files changed (cache the timestamps of when the dependent files changed last).
            // [Option(" ", IsFlag = true)]
            // bool clearOutputFolder = false;
            
            [Named(Description = "The namespace that will replace the original namespace in the client version of the file. By default, the namespace stays as is.")]
            public string? clientNamespaceSubstitute { get; set; } = null;
            
            [Named(Description = "The namespace that will replace the original namespace in the plugin helper file. By default, the namespace stays as is.")]
            public string? pluginNamespaceSubstitute { get; set; } = null;
            
            [Named(Description = "public / internal")]
            public string classVisibility { get; set; } = "internal";
            
            [Named(Description = "Whether it should overwrite the existing generated even if the source file that it was generated from hasn't changed",
                BooleanMode = BooleanMode.Implicit)]
            public bool force { get; set; } = false;
        }

        static int Main(string[] args)
        {
            var app = new AppRunner<Annotator>();
            app.UseNameCasing(Case.CamelCase);
            app.UseDataAnnotationValidations();
            return app.Run(args);
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

        private class AttributeClassWalker : CSharpSyntaxRewriter
        {
            private Options _opts;
            private readonly NamespaceDeclarationSyntax _clientNamespaceSubstitute;
            public NamespaceDeclarationSyntax SourceNamespaceDeclaration { get; private set; }
            public List<ClassDeclarationSyntax> AttributeClasses { get; }
            
            public AttributeClassWalker(Options opts, NamedLogger errorLogger)
            {
                _opts = opts;
                AttributeClasses = new();

                {
                    var a = opts.clientNamespaceSubstitute;
                    if (a is not null)
                    {
                        var nameSyntax = SyntaxFactory.ParseName(a);

                        if (nameSyntax.ContainsDiagnostics)
                        {
                            foreach (var diagnostic in nameSyntax.GetDiagnostics())
                                errorLogger.LogError(diagnostic.GetMessage());
                        }
                        else
                        {
                            _clientNamespaceSubstitute = SyntaxFactory.NamespaceDeclaration(nameSyntax);
                        }
                    }
                }
            }

            private int FindIndexOf(SyntaxTokenList list, SyntaxKind kind)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Kind() == kind)
                        return i;
                }

                return -1;
            }

            public override SyntaxNode VisitNamespaceDeclaration(NamespaceDeclarationSyntax namespaceNode)
            {
                SourceNamespaceDeclaration = namespaceNode;
                return _clientNamespaceSubstitute ?? namespaceNode;
            }
            
            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax classNode)
            {
                if (!classNode.Identifier.Text.EndsWith("Attribute"))
                    return classNode;
                
                var baseList = classNode.BaseList;
                if (baseList is null)
                    return classNode;

                var baseTypes = baseList.Types;
                if (baseTypes.Count == 0)
                    return classNode;

                var baseClass = baseTypes[0].Type;
                {
                    bool isAttribute = baseClass.ToString() is "Attribute" or "System.Attribute";
                    if (!isAttribute)
                        return classNode;
                }
                
                ClassDeclarationSyntax result = classNode;

                if (!_opts.noReplaceInternalWithPublic)
                {
                    var modifiers = classNode.Modifiers;
                    var internalModifierIndex = FindIndexOf(modifiers, SyntaxKind.InternalKeyword);
                    if (internalModifierIndex != -1)
                    {
                        modifiers = modifiers.Replace(
                            modifiers[internalModifierIndex],
                            SyntaxFactory.Token(SyntaxKind.PublicKeyword));
                        result = result.WithModifiers(modifiers);
                    }
                }
                
                AttributeClasses.Add(result);
                
                return result;
            }
        }
        
        [DefaultCommand]
        public async Task<int> Execute(Options opts)
        {
            var logger = new NamedLogger("Annotator");
            opts.targetedFolder = opts.targetedFolder.WithNormalizedDirectorySeparators();
            opts.generatedFilesOutputFolder = opts.generatedFilesOutputFolder?.WithNormalizedDirectorySeparators();
            opts.singleFileOutputName = opts.singleFileOutputName?.WithNormalizedDirectorySeparators();

            // TODO: more error handling won't hurt
            if (opts.generatedFileSuffix == "")
            {
                if (opts.generatedFilesOutputFolder is null)
                {
                    logger.LogError("Both the suffix and the generated subfolder were empty. That means the newly generated files cannot be differentiated from the source files (for sure an error).");
                    return 1;
                }
            }

            var targetRegex = new Regex(opts.targetFileRegex, RegexOptions.IgnoreCase);
            if (opts.targetedFiles is null)
            {
                var ignoreMetric = new ShouldIgnoreDirectory();
                try
                {
                    ignoreMetric.pattern = new Regex(opts.ignoredDirectoriesPattern);
                }
                catch (RegexParseException except)
                {
                    logger.LogError($"The regex {opts.ignoredDirectoriesPattern} failed:\n{except}.");
                    return 1;
                }

                try
                {
                    if (opts.generatedFilesOutputFolder is null 
                        // Non-empty suffix should in theory imply the generated files won't match the
                        // regex that determines files to be processed.
                        || opts.generatedFileSuffix != "")
                    {
                        // sourceFiles = Directory.EnumerateFiles(targetedFolder, "*.cs", SearchOption.AllDirectories);
                    }
                    else // if (generatedFilesOutputFolder is not null)
                    {
                        opts.generatedFilesOutputFolder = Path.GetFullPath(opts.generatedFilesOutputFolder);
                        if (!Directory.Exists(opts.generatedFilesOutputFolder))
                            Directory.CreateDirectory(opts.generatedFilesOutputFolder);
                        ignoreMetric.fullIgnoredPath = opts.generatedFilesOutputFolder;
                    }

                    // We'll be checking out all files, so compiling the regex should be useful? it depends.
                    opts.targetedFiles = FileSystem.EnumerateFilesIgnoring(opts.targetedFolder, ignoreMetric, "*.cs")
                        .Where(f => targetRegex.IsMatch(Path.GetFileNameWithoutExtension(f)))
                        .ToArray();
                }
                catch (RegexParseException except)
                {
                    logger.LogError($"The regex {opts.targetFileRegex} failed:\n{except}.");
                    return 1;
                }
            }

            string GetOutputFilePath(string inputFilePath)
            {
                var generatedFilePath = Path.ChangeExtension(inputFilePath, opts.generatedFileSuffix + ".cs");
                if (opts.generatedFilesOutputFolder is not null)
                    return Path.Join(opts.generatedFilesOutputFolder, Path.GetFileName(generatedFilePath));
                return generatedFilePath;
            }
            
            var builder = CodeBuilder.Create();

            foreach (var inputFilePath in opts.targetedFiles)
            {
                var outputFilePath = GetOutputFilePath(inputFilePath);
                
                // MSBuild checks timestamps instead of content too, I'm pretty sure.
                // This is probably why it rebuilt my plugins, thinking the content changed.
                if (!opts.force)
                {
                    var inputInfo = new FileInfo(inputFilePath);
                    var outputInfo = new FileInfo(outputFilePath);
                    if (inputInfo.LastWriteTime <= outputInfo.LastWriteTime)
                        continue;
                }

                var attributesText = await File.ReadAllTextAsync(inputFilePath, Encoding.UTF8);
                var syntaxTree = CSharpSyntaxTree.ParseText(attributesText);
                var syntaxWalker = new AttributeClassWalker(opts, logger);
                var root = await syntaxTree.GetRootAsync();
                var modifiedSyntaxTree = syntaxWalker.Visit(root);
                var modifiedSyntaxTreeContent = modifiedSyntaxTree.ToString();
                var attributesTextEscaped = modifiedSyntaxTreeContent.Replace("\"", "\"\"");

                builder.Indent();
                builder.Append("namespace ");
                if (opts.pluginNamespaceSubstitute is not null)
                    builder.Append(opts.pluginNamespaceSubstitute);
                else
                    builder.Append(syntaxWalker.SourceNamespaceDeclaration.Name.ToString());
                builder.NewLine();
                builder.StartBlock();

                string classname = Path.GetFileNameWithoutExtension(inputFilePath);
                builder.AppendLine("using Kari.GeneratorCore.Workflow;");
                builder.AppendLine("using Kari.Utils;");
                builder.AppendLine($"{opts.classVisibility} static class Dummy{classname}");
                builder.StartBlock();

                builder.Indent();
                builder.Append($"{opts.classVisibility} const string Text = @\"");
                builder.Append(attributesTextEscaped);
                builder.Append("\";");
                builder.NewLine();
                builder.EndBlock();

                // XxxAttributes -> XxxSymbols
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
                builder.AppendLine($"{opts.classVisibility} static partial class {GetSymbolsClassName()}");
                builder.StartBlock();

                var initializeBuilder = builder.NewWithPreservedIndentation();
                initializeBuilder.AppendLine(opts.classVisibility, " static void Initialize(NamedLogger logger)");
                initializeBuilder.StartBlock();
                initializeBuilder.AppendLine($"var compilation = MasterEnvironment.Instance.Compilation;");

                foreach (var attributeClass in syntaxWalker.AttributeClasses)
                {
                    var attributeName = attributeClass.Identifier.ToString();
                    Assert(!string.IsNullOrEmpty(attributeName));
                    var type = $"AttributeSymbolWrapper<{attributeName}>";
                    builder.AppendLine($"{opts.classVisibility} static {type} {attributeName} {{ get; private set; }}");
                    initializeBuilder.AppendLine($"{attributeName} = new {type}(compilation, logger);");
                }

                initializeBuilder.EndBlock();
                
                builder.NewLine();
                builder.Append(ref initializeBuilder);

                // Apparently, one cannot both do `using` and pass as ref.
                // I say that makes no sense whatsoever.
                initializeBuilder.Dispose();

                builder.EndBlock();
                builder.EndBlock();

                if (opts.singleFileOutputName is null)
                {
                    await using (var fs = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                        fs.Write(builder.AsArraySegment());
                    builder.Clear();
                }
            }

            if (opts.singleFileOutputName is not null)
            {
                opts.singleFileOutputName = Path.ChangeExtension(opts.singleFileOutputName, ".cs");

                string GetPath()
                {
                    if (Path.IsPathRooted(opts.singleFileOutputName))
                        return opts.singleFileOutputName;
                    if (opts.generatedFilesOutputFolder is not null)
                        return Path.Join(opts.generatedFilesOutputFolder, opts.singleFileOutputName);
                    return Path.Join(opts.targetedFolder, opts.singleFileOutputName);
                }

                string outputFilePath = GetPath();
                Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
                await File.WriteAllTextAsync(outputFilePath, outputFilePath);
            }
            else
            {
                Assert(builder.StringBuilder.Length == 0);
            }

            return 0;
        }
    }
}