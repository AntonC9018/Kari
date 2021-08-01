using Kari.GeneratorCore.Workflow;

namespace Kari.Plugins.Terminal
{
    public static class CommandSymbols
    {
        public static AttributeSymbolWrapper<CommandAttribute> CommandAttribute { get; private set; } 
        public static AttributeSymbolWrapper<FrontCommandAttribute> FrontCommandAttribute { get; private set; } 
        public static AttributeSymbolWrapper<OptionAttribute> OptionAttribute { get; private set; } 
        public static AttributeSymbolWrapper<ArgumentAttribute> ArgumentAttribute { get; private set; } 

        public static void Initialize() 
        { 
            var compilation = MasterEnvironment.Instance.Compilation;
			CommandAttribute		= new AttributeSymbolWrapper<CommandAttribute>	    (compilation);
			FrontCommandAttribute 	= new AttributeSymbolWrapper<FrontCommandAttribute> (compilation);
			OptionAttribute			= new AttributeSymbolWrapper<OptionAttribute>	    (compilation);
			ArgumentAttribute		= new AttributeSymbolWrapper<ArgumentAttribute>	    (compilation);
        }
    }
}