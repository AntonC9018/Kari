
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kari.Generator
{
    internal static class PseudoCompilation
    {
        internal static CSharpCompilation CreateFromDirectory(string directoryRoot, string generatedFolderPrefix, CancellationToken cancellationToken)
        {
            var parseOption = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);

            var syntaxTrees = new List<SyntaxTree>();
            var generatedFolder = Path.GetFullPath(generatedFolderPrefix);
            foreach (var file in IterateCsFileWithoutBinObjIgnoringFolder(directoryRoot, generatedFolder))
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                var syntax = CSharpSyntaxTree.ParseText(text, parseOption);
                syntaxTrees.Add(syntax);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var metadata = GetStandardReferences().Select(x => MetadataReference.CreateFromFile(x)).ToArray();

            var compilation = CSharpCompilation.Create(
                "CodeGenTemp",
                syntaxTrees,
                DistinctReference(metadata),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

            return compilation;
        }

        private static IEnumerable<MetadataReference> DistinctReference(IEnumerable<MetadataReference> metadataReferences)
        {
            var set = new HashSet<string>();
            foreach (var item in metadataReferences)
            {
                if (item.Display is object && set.Add(Path.GetFileName(item.Display)))
                {
                    yield return item;
                }
            }
        }

        private static List<string> GetStandardReferences()
        {
            var standardMetadataType = new[]
            {
                typeof(object),
                typeof(Attribute),
                typeof(Enumerable),
                typeof(Task<>),
                typeof(IgnoreDataMemberAttribute),
                typeof(System.Collections.Hashtable),
                typeof(System.Collections.Generic.List<>),
                typeof(System.Collections.Generic.HashSet<>),
                typeof(System.Collections.Immutable.IImmutableList<>),
                typeof(System.Linq.ILookup<,>),
                typeof(System.Tuple<>),
                typeof(System.ValueTuple<>),
                typeof(System.Collections.Concurrent.ConcurrentDictionary<,>),
                typeof(System.Collections.ObjectModel.ObservableCollection<>),
            };

            var metadata = standardMetadataType
               .Select(x => x.Assembly.Location)
               .Distinct()
               .ToList();

            var dir = new FileInfo(typeof(object).Assembly.Location).Directory;
            {
                var path = Path.Combine(dir!.FullName, "netstandard.dll");
                if (File.Exists(path))
                {
                    metadata.Add(path);
                }
            }

            {
                var path = Path.Combine(dir.FullName, "System.Runtime.dll");
                if (File.Exists(path))
                {
                    metadata.Add(path);
                }
            }

            return metadata;
        }

        private static IEnumerable<string>? CleanPreprocessorSymbols(IEnumerable<string>? preprocessorSymbols)
        {
            return preprocessorSymbols?.Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private static IEnumerable<string> IterateCsFileWithoutBinObjIgnoringFolder(string root, string ingoredFolderName)
        {
            foreach (var item in Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly))
            {
                yield return item;
            }

            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                var dirName = new DirectoryInfo(dir).Name;
                if (dirName == "bin" || dirName == "obj")
                {
                    continue;
                }
                if (ingoredFolderName != dir)
                {
                    foreach (var item in IterateCsFileWithoutBinObjIgnoringFolder(dir, ingoredFolderName))
                    {
                        yield return item;
                    }
                }
            }
        }
    }
}
