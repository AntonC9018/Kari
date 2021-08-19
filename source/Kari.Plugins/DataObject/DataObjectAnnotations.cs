namespace Kari.Plugins.DataObject
{
    using System;
    using System.Diagnostics;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    [Conditional("CodeGeneration")]
    public class DataObjectAttribute : Attribute
    {
    }
}
