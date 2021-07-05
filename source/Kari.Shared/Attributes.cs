namespace Kari.Shared
{
    using System;
    using System.Diagnostics;

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    [Conditional("CodeGeneration")]
    public class KariTestAttribute : Attribute
    {
        public KariTestAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; } 
    }

    [AttributeUsage(AttributeTargets.Delegate)]
    [Conditional("CodeGeneration")]
    public class KariWeirdDetectionAttribute : Attribute{}
}
