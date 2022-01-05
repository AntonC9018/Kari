/*

This file exists to check out how fast Roslyn processes source files.
On my machine, it processes all Kari source files (105 symbols, 6.8k LOC)
in 1 to 2 seconds (the sync version being slower than the async one by ~35%).

Now, I feel like this is too slow and I must be doing something wrong at
a more fundamental / API level.

*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kari.Arguments;
using Kari.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kari.Test;

class KariTest
{
    class Options
    {
        [Option("Path to root directory to analyze all source files in.",
            IsRequired = true,
            IsPath = true)]
        public string path;

        public enum AnalysisMode
        {
            Sync, Async
        }
        [Option("Analysis threading mode")]
        public AnalysisMode mode = AnalysisMode.Async;

        [Option("Whether to force it to load the root of the syntax tree when loading files vs when querying symbols. Only relevant when in async mode.")]
        public bool loadRootNodeOnFileLoad = false;

        [Option("Whether to print all found types.")]
        public bool printTypes = false;
    }

    static async Task<int> Main(string[] args)
    {
        var logger = new NamedLogger("Test");
        var options = new Options();
        var parser = ParserHelpers.DoSimpleParsing(options, args, logger);
        if (logger.AnyHasErrors)
            return 1;
        if (parser.IsHelpSet || parser.IsEmpty)
            return 0;
        
        logger.Log("Mode: " + options.mode.ToString());
        logger.Log("LoadRootNodeOnFileLoad: " + options.loadRootNodeOnFileLoad.ToString());
        logger.Log("SourceFolder: " + options.path);

        Measurer measurer = new Measurer(logger);
        measurer.Start("Stuff");
        TypeLists typeLists;
        {
            if (options.mode == Options.AnalysisMode.Sync)
                typeLists = StuffSync(options.path);
            else
                typeLists = await StuffAsync(options.path, options.loadRootNodeOnFileLoad);
        }
        measurer.Stop();

        var types = typeLists.Symbols.SelectMany(t => t);

        logger.Log($"Found {types.Count()} symbols.");
        logger.Log($"Total lines of code read: {LineCounter}");

        Console.WriteLine(String.Join(", ", types.Select(t => t.Name)));
        
        return 0;
    }

    static long LineCounter = 0;
    static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest, 
        DocumentationMode.None, SourceCodeKind.Regular);
    static readonly CSharpCompilationOptions CompilationOptions = new CSharpCompilationOptions(
            outputKind: OutputKind.DynamicallyLinkedLibrary, 
            reportSuppressedDiagnostics: false, 
            generalDiagnosticOption: ReportDiagnostic.Suppress);

    

    record struct TypeLists(List<INamedTypeSymbol>[] Symbols);


    static Compilation DoCompilation(IEnumerable<SyntaxTree> trees)
    {
        // Creating the compilation takes no time.
        // Speaking of metadata references, I have tested it without them yet.
        var c = CSharpCompilation.Create("Test", trees, null, CompilationOptions);
        return c;
    }

    // This function is nice because in the end the syntax trees, and the resulting symbols
    // end up organized in arrays, so I think I will go for this one.
    static async Task<TypeLists> StuffAsync(string directory, bool loadRootOnFileLoad)
    {
        Measurer measurer = new Measurer(new NamedLogger("StuffAsync"));

        var subdirectoryNames = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly);
        var count = subdirectoryNames.Length;
        var syntaxTreeArrays = new SyntaxTree[count][];
        var syntaxTreeTasks = new Task<SyntaxTree>[count][];
        
        measurer.Start("Syntax Trees");
        {
            for (int i = 0; i < count; i++)
            {
                var files = Directory.GetFiles(subdirectoryNames[i], "*.cs", SearchOption.AllDirectories);
                syntaxTreeArrays[i] = new SyntaxTree[files.Length];
                syntaxTreeTasks[i] = LoadSyntaxTrees(files, loadRootOnFileLoad);

                static Task<SyntaxTree>[] LoadSyntaxTrees(string[] filePaths, bool loadRootOnFileLoad)
                {
                    var result = new Task<SyntaxTree>[filePaths.Length];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = LoadSyntaxTree(filePaths[i], loadRootOnFileLoad);
                    return result;

                    static async Task<SyntaxTree> LoadSyntaxTree(string filePath, bool loadRootOnFileLoad)
                    {
                        var t = await File.ReadAllTextAsync(filePath);
                        var tree = CSharpSyntaxTree.ParseText(t, ParseOptions, filePath);

                        if (loadRootOnFileLoad)
                            await tree.GetRootAsync();

                        Interlocked.Add(ref LineCounter, t.Count(a => a == '\n'));
                        
                        return tree;
                    }
                }
            }
            for (int i = 0; i < count; i++)
            for (int j = 0; j < syntaxTreeArrays[i].Length; j++)
            {
                syntaxTreeArrays[i][j] = await syntaxTreeTasks[i][j];
            }
        }
        measurer.Stop();

        measurer.Start("Compilation");
        var compilation = DoCompilation(syntaxTreeArrays.SelectMany(t => t));
        measurer.Stop();

        var typesTasks = new Task<List<INamedTypeSymbol>>[count];
        var types = new List<INamedTypeSymbol>[count];

        measurer.Start("Collecting Symbols");
        {
            for (int i = 0; i < count; i++)
            {
                typesTasks[i] = GetTypes(compilation, syntaxTreeArrays[i]);

                static async Task<List<INamedTypeSymbol>> GetTypes(Compilation compilation, SyntaxTree[] syntaxTrees)
                {
                    var result = new List<INamedTypeSymbol>();
                    foreach (var t in syntaxTrees)
                    {
                        var model = compilation.GetSemanticModel(t, ignoreAccessibility: true);
                        var root = await t.GetRootAsync();

                        foreach (var tds in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                        {
                            var s = model.GetDeclaredSymbol(tds);
                            result.Add(s);
                        }
                    }
                    return result;
                }
            }
            for (int i = 0; i < count; i++)
                types[i] = await typesTasks[i];
        }
        measurer.Stop();

        return new TypeLists(types);
    }

    static TypeLists StuffSync(string directory)
    {
        Measurer measurer = new Measurer(new NamedLogger("StuffSync"));

        var trees = new List<SyntaxTree>();
        
        measurer.Start("Syntax Trees");
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            LineCounter += text.Count(a => a == '\n');
            var syntaxTree = CSharpSyntaxTree.ParseText(text, ParseOptions);
            trees.Add(syntaxTree);
        }
        measurer.Stop();
        
        measurer.Start("Compilation");
        var compilation = DoCompilation(trees);
        measurer.Stop();

        // There is no clear way how to split it by source file directories.
        // I guess the most fait approach would be to check the Location of the symbol 
        // and getting the directory one level relative to root, but that's kind of complicated.
        measurer.Start("Collecting Symbols");
        var result = new TypeLists(new[] { GetTypesOfNamespace(compilation.GlobalNamespace).ToList() });
        measurer.Stop();

        return result;

        static IEnumerable<INamedTypeSymbol> GetTypesOfNamespace(INamespaceSymbol nspace)
        {
            foreach (var ns in nspace.GetMembers().OfType<INamespaceSymbol>())
            foreach (var t in GetTypesOfNamespace(ns))
                yield return t;

            foreach (var t in GetTypesOfNamespaceOrType(nspace))
                yield return t;
        }

        static IEnumerable<INamedTypeSymbol> GetTypesOfNamespaceOrType(INamespaceOrTypeSymbol type)
        {
            foreach (var t in type.GetMembers().OfType<INamedTypeSymbol>())
            {
                yield return t;
                foreach (var tt in GetTypesOfNamespaceOrType(t))
                    yield return tt;
            }
        }
    }
}
