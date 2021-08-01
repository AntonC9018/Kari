using Kari.GeneratorCore.Workflow;
using Microsoft.CodeAnalysis;

namespace Kari.Plugins.Flags
{
    public static class FlagsSymbols
    {
        public static AttributeSymbolWrapper<NiceFlagsAttribute> NiceFlagsAttribute { get; private set; }
        public static void Initialize() 
        { 
            Compilation compilation = MasterEnvironment.Instance.Compilation;
            NiceFlagsAttribute = new AttributeSymbolWrapper<NiceFlagsAttribute>(compilation);
        }
    }
}