using System.Collections.Generic;
using System.Threading.Tasks;
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

        public Task CollectInfo(ProjectEnvironment environment)
        {
            // It should be able to crank through those symbols fast on its own, so this
            // Task.Run is debatable.
            return Task.Run(() => {
                foreach (var t in environment.TypesWithAttributes)
                {
                    if (t.HasAttribute(environment.Symbols.NiceFlagsAttribute.symbol))
                    {
                        _infos.Add(new FlagsInfo(t));
                    }
                }
            });
        }
    }
}