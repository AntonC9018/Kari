using Kari.GeneratorCore.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Kari.Plugins.Flags
{
    public static class FlagsSymbols
    {
        public static AttributeSymbolWrapper<NiceFlagsAttribute> NiceFlagsAttribute { get; private set; }
        public static void Initialize() 
        { 
            Compilation compilation = MasterEnvironment.SingletonInstance.Compilation;
            NiceFlagsAttribute = new AttributeSymbolWrapper<NiceFlagsAttribute>(compilation);
        }
    }
}