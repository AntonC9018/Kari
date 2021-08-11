namespace Kari.Plugins.Terminal
{
    using System;
    using System.Diagnostics;

    public interface ICommandAttribute
    {
        string Name { get; set; } 
        string Help { get; set; } 
        string ShortHelp { get; set; } 
    }

    [AttributeUsage(AttributeTargets.Method)]
    [Conditional("CodeGeneration")]
    public class CommandAttribute : Attribute, ICommandAttribute
    {
        public CommandAttribute()
        {
        }

        public CommandAttribute(string name, string help)
        {
            Name = name;
            Help = help;
        }

        public string Name { get; set; } 
        private string _help;
        public string Help 
        { 
            get => _help; 
            set { ShortHelp ??= value; _help = value; } 
        } 
        public string ShortHelp { get; set; } 
    }


    [AttributeUsage(AttributeTargets.Method)]
    [Conditional("CodeGeneration")]
    public class FrontCommandAttribute : Attribute, ICommandAttribute
    {
        public FrontCommandAttribute()
        {
        }

        public FrontCommandAttribute(string name, string help)
        {
            Name = name;
            Help = help;
        }

        public string Name { get; set; } 
        private string _help;
        public string Help 
        { 
            get => _help; 
            set { ShortHelp ??= value; _help = value; } 
        } 
        public string ShortHelp { get; set; } 
        public int MinimumNumberOfArguments { get; set; } = 0;
        public int MaximumNumberOfArguments { get; set; } = -1;
        public int NumberOfArguments {
            get => MinimumNumberOfArguments;
            set {
                MinimumNumberOfArguments = value;
                MaximumNumberOfArguments = value;
            }
        }
    }

    public interface IArgument
    {
        string Name { get; set; }
        string Parser { get; set; }
        string Help { get; set; }
    }

    public static class Validators
    {
        public static string OptionName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new Exception("Name must not be an empty string.");
            }
            if (value[0] == '-')
            {
                value = value.Substring(1);
            }
            if (!char.IsLetter(value[0]) && value[0] != '_')
            {
                throw new Exception($"Name must start with a letter or '_' ({value}).");
            }
            return value;
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    [Conditional("CodeGeneration")]
    public class OptionAttribute : Attribute, IArgument
    {
        public string _name;
        public string Name 
        { 
            get => _name; 
            set => _name = Validators.OptionName(value);
        }
        public string Help { get; set; }
        public bool IsFlag { get; set; }
        public string Parser { get; set; }

        public OptionAttribute(string name, string help)
        {
            Name = name;
            Help = help;
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    [Conditional("CodeGeneration")]
    public class ArgumentAttribute : Attribute, IArgument
    {
        public string _name;
        public string Name 
        { 
            get => _name; 
            set => _name = Validators.OptionName(value);
        }
        public bool IsOptionLike { get; set; }
        public string Help { get; set; }
        public string Parser { get; set; }

        public ArgumentAttribute(string help)
        {
            Help = help;
        }
        
        public ArgumentAttribute(string name, string help)
        {
            IsOptionLike = true;
            Name = name;
            Help = help;
        }
    }
}