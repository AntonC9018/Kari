using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Test
{
    using Kari.GeneratorCore;
    using Kari.GeneratorCore.Workflow;

    public class HelloAttribute : System.Attribute
    {
        public int i;
        public HelloAttribute(int num) { i = num; }
    }

    class Program
    {
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
        
        static void Main(string[] args)
        {
            string source = @"
namespace Test 
{ 
    [Hello(123)]
    public class World{}
}";
            string attribute = @"
namespace Test 
{ 
    public class HelloAttribute : System.Attribute
    {
        public int i;
        public HelloAttribute(int num) { i = num; }
    }
}";
            Type[] ts = { typeof(object), typeof(Attribute) };

            var metadata = ts
               .Select(x => x.Assembly.Location)
               .Distinct()
               .ToList();

            var distinct = DistinctReference(metadata.Select(x => MetadataReference.CreateFromFile(x)).ToArray());

            var sourceSyntaxTree = CSharpSyntaxTree.ParseText(source);
            var attributeSyntaxTree = CSharpSyntaxTree.ParseText(attribute);
            var syntaxTrees = new List<SyntaxTree> { 
                sourceSyntaxTree,
                // attributeSyntaxTree,
            };

            var compilation = CSharpCompilation.Create(
                "CodeGenTemp",
                syntaxTrees,
                distinct,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

            compilation = compilation.AddSyntaxTrees(attributeSyntaxTree);

            var world = compilation.GetSymbolsWithName("World").Single();
            var attrClass = compilation.GetSymbolsWithName("HelloAttribute").Single();
            var attrInstantiation = world.GetAttributes()[0].MapToType<Test.HelloAttribute>();
        }
    }
}
