using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Test
{
    using System.Reflection;
    using Kari.GeneratorCore;
    using Kari.GeneratorCore.Workflow;

    
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
public class HelloAttribute : System.Attribute
{
    public int i;
    public HelloAttribute() {}
    public HelloAttribute(int num) { i = num; }
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

            var dll = Assembly.LoadFrom(@"E:\Coding\C#\some_project\Build\bin\Kari.OtherTest\Debug\netcoreapp3.1\Kari.OtherTest.dll");
            var runClass = dll.GetExportedTypes().Single(type => type.Name == "Run");
            ((MethodInfo) runClass.GetMember("RunThing").Single()).Invoke(null, new object[] { world });
        }
    }
}
