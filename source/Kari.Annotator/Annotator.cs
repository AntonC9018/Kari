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
using Kari.GeneratorCore.Workflow;

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

        private static readonly CSharpCompilationOptions _CompilationOptions = new CSharpCompilationOptions(
            outputKind: OutputKind.DynamicallyLinkedLibrary, 
            allowUnsafe: true,
            reportSuppressedDiagnostics: false,
            concurrentBuild: true,
            generalDiagnosticOption: ReportDiagnostic.Suppress);

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
            private readonly NameSyntax _clientNamespaceSubstitute;
            public NamespaceDeclarationSyntax SourceNamespaceDeclaration { get; private set; }
            
            public AttributeClassWalker(Options opts, NamedLogger errorLogger)
            {
                _opts = opts;

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
                            _clientNamespaceSubstitute = nameSyntax;
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
                if (_clientNamespaceSubstitute is not null)
                    namespaceNode = namespaceNode.WithName(_clientNamespaceSubstitute);
                return base.VisitNamespaceDeclaration(namespaceNode);
            }

            public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax usingDirective)
            {
                if (usingDirective.Name.ToString().Contains("Microsoft.CodeAnalysis"))
                    return null;
                return usingDirective;
            }

            private SyntaxNode MaybeGetSystemType(SyntaxToken identifier)
            {
                if (identifier.Text is "ITypeSymbol" or "INamedTypeSymbol")
                {
                    return SyntaxFactory.QualifiedName(
                        SyntaxFactory.IdentifierName("System"),
                        SyntaxFactory.IdentifierName("Type"));
                }
                return null;
            }

            public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax name)
            {
                return MaybeGetSystemType(name.Right.Identifier)?.WithTriviaFrom(name)
                    ?? base.VisitQualifiedName(name);
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax name)
            {
                return MaybeGetSystemType(name.Identifier)?.WithTriviaFrom(name)
                    ?? base.VisitIdentifierName(name);
            }

            public static bool IsAttribute(ClassDeclarationSyntax classNode)
            {
                if (!classNode.Identifier.Text.EndsWith("Attribute"))
                    return false;
                
                var baseList = classNode.BaseList;
                if (baseList is null)
                    return false;

                var baseTypes = baseList.Types;
                if (baseTypes.Count == 0)
                    return false;

                var baseClass = baseTypes[0].Type;
                {
                    bool isAttribute = baseClass.ToString() is "Attribute" or "System.Attribute";
                    if (!isAttribute)
                        return false;
                }

                return true;
            }
            
            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax classNode)
            {
                if (!IsAttribute(classNode))
                    return base.VisitClassDeclaration(classNode);
                
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
                return base.VisitClassDeclaration(result);
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
            
            var standardMetadataType = new[]
            {
                typeof(object),
                typeof(System.Attribute),
                typeof(INamedTypeSymbol),
                typeof(ITypeSymbol),
            };
            var metadata = standardMetadataType
                .Select(t => t.Assembly.Location)
                .Distinct()
                .Select(t => MetadataReference.CreateFromFile(t));
                
            var b = CodeBuilder.Create();

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
                var modifiedSyntaxRoot = syntaxWalker.Visit(root);
                var modifiedSyntaxTree = modifiedSyntaxRoot.SyntaxTree;
                var modifiedSyntaxTreeContent = modifiedSyntaxRoot.ToString();
                var attributesTextEscaped = modifiedSyntaxTreeContent.Replace("\"", "\"\"");

                var compilation = CSharpCompilation.Create("Annotating", new[]{syntaxTree}, metadata, _CompilationOptions);
                var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);

                b.Indent();
                b.Append("namespace ");
                if (opts.pluginNamespaceSubstitute is not null)
                    b.Append(opts.pluginNamespaceSubstitute);
                else
                    b.Append(syntaxWalker.SourceNamespaceDeclaration.Name.ToString());
                b.NewLine();
                b.StartBlock();

                string classname = Path.GetFileNameWithoutExtension(inputFilePath);
                b.AppendLine("using Kari.GeneratorCore.Workflow;");
                b.AppendLine("using Kari.Utils;");
                b.AppendLine("using Microsoft.CodeAnalysis;");
                b.AppendLine($"{opts.classVisibility} static class Dummy{classname}");
                b.StartBlock();

                b.Indent();
                b.Append($"{opts.classVisibility} const string Text = @\"");
                b.Append(attributesTextEscaped);
                b.Append("\";");
                b.NewLine();
                b.EndBlock();

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
                b.AppendLine($"{opts.classVisibility} static partial class {GetSymbolsClassName()}");
                b.StartBlock();

                foreach (var attributeClass in 
                    syntaxTree.GetRoot()
                    // modifiedSyntaxRoot
                        .DescendantNodesAndSelf()
                        .OfType<ClassDeclarationSyntax>()
                        .Where(s => AttributeClassWalker.IsAttribute(s)))
                {
                    var typeSymbol = semanticModel.GetDeclaredSymbol(attributeClass);
                    GenerateGetMethodsForAttributeType(typeSymbol, compilation, ref b);
                }

                b.EndBlock();
                b.EndBlock();

                if (opts.singleFileOutputName is null)
                {
                    await using (var fs = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                        fs.Write(b.AsArraySegment());
                    b.Clear();
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
                Assert(b.StringBuilder.Length == 0);
            }

            return 0;
        }

        public static void GenerateGetMethodsForAttributeType(INamedTypeSymbol attributeType, Compilation compilation, ref CodeBuilder b)
        {
            b.AppendLine($"public static {attributeType.Name} Get{attributeType.Name}("
                + "this ITypeSymbol type, Compilation compilation, System.Action<string> errorHandler)");
            b.StartBlock();

            string t = $@"
                var attributeType = compilation.GetTypeByMetadataName(""{attributeType.GetFullyQualifiedName()}"");
                if (attributeType is null)
                {{
                    errorHandler(""Attribute type {attributeType.Name} not found."");
                    return null;
                }}
                var attributes = type.GetAttributes();

                AttributeData attribute = null;
                {{
                    foreach (var a in attributes)
                    {{
                        if (a.AttributeClass == attributeType)
                            attribute = a; 
                    }}
                    if (attribute is null)
                        return null;
                }}
                var constructor = attribute.AttributeConstructor;
                var constructors = attributeType.InstanceConstructors;
                var args = attribute.ConstructorArguments;
                var application = (Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax) attribute.ApplicationSyntaxReference.GetSyntax();
                
                if (constructor is null)
                {{
                    errorHandler($""No constructors matched at {{application.GetLocationInfo()}}."");
                    return null;
                }}
                
                {attributeType.Name} result;";
            b.Append(t);
            b.NewLine();

            static bool IsType(string name)
            {
                return name == "INamedTypeSymbol" || name == "ITypeSymbol";
            }

            static void AppendGetParam(ref CodeBuilder b, ITypeSymbol type, string valueArgument)
            {
                if (type is IArrayTypeSymbol arrayType)
                {
                    var name = arrayType.ElementType.GetFullyQualifiedName();
                    b.Append($"SyntaxHelper.Array<{name}>({valueArgument}, errorHandler)");
                }
                else
                {
                    b.Append($"({type.GetFullyQualifiedName()}) {valueArgument}.Value");
                }
                b.Append(";");
                b.NewLine();
            }

            var constructors = attributeType.InstanceConstructors;

            if (constructors.Length == 1
                && constructors[0].IsImplicitlyDeclared)
            {
                b.AppendLine($"result = new {attributeType.Name}();");
            }
            else
            {
                for (int i = 0; i < constructors.Length; i++)
                {
                    b.Indent();
                    if (i != 0)
                        b.Append("else ");
                    b.Append($"if (constructor == constructors[{i}])");
                    b.NewLine();
                    b.StartBlock();
                    var constructor = constructors[i];

                    string GetParamName(string name) =>  "_" + name;

                    b.AppendLine("// get args");
                    var parameters = constructor.Parameters;
                    for (int argIndex = 0; argIndex < parameters.Length; argIndex++)
                    {
                        var p = parameters[argIndex];
                        var paramName = GetParamName(p.Name);
                        var paramType = p.Type;

                        b.Indent();
                        b.Append($"var {paramName} = ");
                        AppendGetParam(ref b, paramType, $"args[{argIndex}]");
                    }

                    b.AppendLine();
                    b.AppendLine("// construct");

                    b.Indent();
                    b.Append($"result = new {attributeType.Name}(");
                    var list = CodeListBuilder.Create(", ");
                    foreach (var p in parameters)
                        list.AppendOnSameLine(ref b, GetParamName(p.Name));
                    b.Append(");");
                    b.NewLine();

                    b.EndBlock();
                }

                b.AppendLine("else");
                b.StartBlock();
                b.AppendLine("errorHandler($\"No valid constructor overload at {application.GetLocationInfo()}.\");");
                b.AppendLine("return null;");
                b.EndBlock();
            }

            bool CheckAccessibility(Accessibility a)
            {
                return a is Accessibility.Public or Accessibility.Internal;
            }

            var setProperties = attributeType
                .GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.SetMethod is not null
                    && CheckAccessibility(p.DeclaredAccessibility));
            var settableFields = attributeType
                .GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => !f.IsReadOnly
                    && CheckAccessibility(f.DeclaredAccessibility));

            if (setProperties.Any() || settableFields.Any())
            {
                b.AppendLine("foreach (var namedArgument in attribute.NamedArguments)");
                b.StartBlock();
                b.AppendLine("switch (namedArgument.Key)");
                b.StartBlock();
                
                b.AppendLine("default:");
                b.StartBlock();
                b.AppendLine("errorHandler($\"No such property {namedArgument.Key}.\");");
                b.AppendLine("return null;");
                b.EndBlock();

                static void AppendCase(ref CodeBuilder b, ITypeSymbol type, string name)
                {
                    b.AppendLine("case nameof(result.", name, "): ");
                    b.StartBlock();
                    b.Indent();
                    b.Append($"result.{name} = ");
                    AppendGetParam(ref b, type, "namedArgument.Value");
                    b.AppendLine("break;");
                    b.EndBlock();
                }

                foreach (var p in setProperties)
                    AppendCase(ref b, p.Type, p.Name);
                foreach (var f in settableFields)
                    AppendCase(ref b, f.Type, f.Name);
                
                b.EndBlock();
                b.EndBlock();
            }

            b.AppendLine("return result;");
            b.EndBlock();

            // A bunch of overloads
            b.AppendLine($"public static bool TryGet{attributeType.Name}("
                + $"this ITypeSymbol type, Compilation compilation, NamedLogger logger, out {attributeType.Name} attr)");
            b.StartBlock();
            b.AppendLine($"attr = Get{attributeType.Name}(type, compilation, s => logger.LogError(s));");
            b.AppendLine("return attr is not null;");
            b.EndBlock();

            b.AppendLine($"public static bool TryGet{attributeType.Name}("
                + $"this ITypeSymbol type, Compilation compilation, out {attributeType.Name} attr)");
            b.StartBlock();
            b.AppendLine($"attr = Get{attributeType.Name}(type, compilation, s => System.Console.WriteLine(s));");
            b.AppendLine("return attr is not null;");
            b.EndBlock();

            b.AppendLine($"public static {attributeType.Name} Get{attributeType.Name}("
                + "this ITypeSymbol type, Compilation compilation)");
            b.StartBlock();
            b.AppendLine($"return Get{attributeType.Name}(type, compilation, s=>{{}});");
            b.EndBlock();

            b.AppendLine($"public static {attributeType.Name} Get{attributeType.Name}("
                + "this ITypeSymbol type, Compilation compilation, NamedLogger logger)");
            b.StartBlock();
            b.AppendLine($"return Get{attributeType.Name}(type, compilation, s => logger.LogError(s));");
            b.EndBlock();
        }
    }
}