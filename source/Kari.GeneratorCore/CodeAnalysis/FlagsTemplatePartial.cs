using System.Collections.Generic;
using Kari.GeneratorCore.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore
{
    public class FlagsInfo
    {
        public FlagsInfo(INamedTypeSymbol symbol)
        {
            Name = symbol.Name;
            FullName = symbol.GetFullyQualifiedName();
        }

        public readonly string Name;
        public readonly string FullName;
    }

    public partial class FlagsTemplate
    {
        public readonly List<FlagsInfo> _infos = new List<FlagsInfo>();

        public void CollectInfo(Environment environment)
        {
            foreach (var t in environment.TypesWithAttributes)
            {
                if (t.HasAttribute(environment.Symbols.NiceFlagsAttribute.symbol))
                {
                    _infos.Add(new FlagsInfo(t));
                }
            }
        }
    }
}