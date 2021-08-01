namespace Kari.Plugins.Flags
{
    using System;
    using System.Diagnostics;

    [AttributeUsage(AttributeTargets.Enum)]
    [Conditional("CodeGeneration")]
    public class NiceFlagsAttribute : FlagsAttribute
    {
    }
}