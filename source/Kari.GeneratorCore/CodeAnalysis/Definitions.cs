using System;
using System.Collections.Generic;
using System.Linq;

#pragma warning disable SA1649 // File name should match first type name

namespace Kari.GeneratorCore.CodeAnalysis
{
    public class GeneralInfo
    {
        public string Namespace { get; set; }

        public string Name { get; set; }

        public string FullName { get; set; }
    }
}
