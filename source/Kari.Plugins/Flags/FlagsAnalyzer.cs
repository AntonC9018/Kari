using System.Collections.Generic;
using System.Threading.Tasks;
using Kari.GeneratorCore.Workflow;
using Microsoft.CodeAnalysis;

namespace Kari.Plugins.Flags
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

    public partial class FlagsAnalyzer : IAnalyzer
    {
        public readonly List<FlagsInfo> _infos = new List<FlagsInfo>();

        public void Collect(ProjectEnvironment environment)
        {
            // It should be able to crank through those symbols fast on its own, so this
            // Task.Run is debatable.
            foreach (var t in environment.TypesWithAttributes)
            {
                if (t.HasAttribute(FlagsSymbols.NiceFlagsAttribute.symbol))
                {
                    _infos.Add(new FlagsInfo(t));
                }
            }
        }
    }
}