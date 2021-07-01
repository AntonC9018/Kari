using System;
using System.Diagnostics;
using CodeGeneration.Roslyn;
using static Kari.Shared.Constants;

namespace Kari.Shared
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    [CodeGenerationAttribute(KariGenerators + ".TestGenerator, " + KariGenerators)]
    [Conditional(AtCodeGeneration)]
    public class KariTestAttribute : Attribute
    {
        public KariTestAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; } 
    }
}
