namespace Kari
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

    [AttributeUsage(AttributeTargets.Method)]
    [Conditional("CodeGeneration")]
    public class CommandAttribute : Attribute
    {
        public CommandAttribute(string name, string help)
        {
            Name = name;
            Help = help;
        }

        public string Name { get; } 
        public string Help { get; set; } 
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    [Conditional("CodeGeneration")]
    public class OptionAttribute : Attribute
    {
        public string Name { get; set; }
        public string Help { get; set; }
        public bool IsFlag { get; set; }

        public OptionAttribute(string name, string help, bool isFlag = false)
        {
            Name = name;
            Help = help;
            IsFlag = isFlag;
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    [Conditional("CodeGeneration")]
    public class ArgumentAttribute : Attribute
    {
        public string Name { get; set; }
        public bool IsOptionLike => Name != null;
        public string Help { get; set; }

        public ArgumentAttribute(string help)
        {
            Help = help;
        }
        
        public ArgumentAttribute(string name, string help)
        {
            Name = name;
            Help = help;
        }
    }
}
