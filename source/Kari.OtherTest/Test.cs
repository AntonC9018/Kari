using Kari.GeneratorCore.Workflow;
using Microsoft.CodeAnalysis;

namespace Test
{
    public class HelloAttribute : System.Attribute
    {
        public int i;
        public HelloAttribute() {}
        public HelloAttribute(int num) { i = num; }
    }

    public static class Run
    {
        public static void RunThing(ISymbol world)
        {
            var attrInstantiation = world.GetAttributes()[0].MapToType<Test.HelloAttribute>();
        }
    }
}