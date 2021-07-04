
namespace Kari.GeneratorCore.CodeAnalysis
{
	public static class DummyAttributes
	{
		public const string Text = @"namespace Kari.Shared
{
    using System;
    using System.Diagnostics;

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    [Conditional(""CodeGeneration"")]
    public class KariTestAttribute : Attribute
    {
        public KariTestAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; } 
    }
}
";
	}
}