using Kari.GeneratorCore.CodeAnalysis;

namespace Kari.Plugins.Terminal
{
    public static class ParserSymbols
    {
        public static AttributeSymbolWrapper<ParserAttribute> ParserAttribute { get; private set; }
        public static void Initialize() 
        { 
            var compilation = MasterEnvironment.SingletonInstance.Compilation;
            ParserAttribute = new AttributeSymbolWrapper<ParserAttribute>(compilation);
        }
    }
}