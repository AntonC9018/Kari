namespace Kari.Plugins.Terminal
{
    using System;
    using System.Diagnostics;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field)]
    [Conditional("CodeGeneration")]
    internal class ParserAttribute : Attribute
    {
        public string Name { get; set; }

        public ParserAttribute()
        {
        }

        public ParserAttribute(string name)
        {
            Name = name;
        }
    }
}