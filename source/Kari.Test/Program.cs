using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kari.Generated;
using Kari.Plugins.Terminal;

namespace Kari.Generated
{
    public class CommandContext : IDuckCommandContext
    {
        public bool HasErrors => throw new NotImplementedException();

        public void EndParsing()
        {
            throw new NotImplementedException();
        }

        public T ParseArgument<T>(int argumentIndex, string argumentName, IValueParser<T> parser)
        {
            throw new NotImplementedException();
        }

        public bool ParseFlag(string optionName, bool defaultValue = false, bool flagValue = true)
        {
            throw new NotImplementedException();
        }

        public T ParseOption<T>(string optionName, IValueParser<T> parser)
        {
            throw new NotImplementedException();
        }

        public T ParseOption<T>(string optionName, T defaultValue, IValueParser<T> parser)
        {
            throw new NotImplementedException();
        }
    }
}

namespace Kari.Test
{
    class Program
    {
        [FrontCommand("Hello", "World")]
        public static void Func(CommandContext ctx)
        {

        }

        [Command("join", "123")]
        public static void Hello(int i)
        {
        }

        static void Main(string[] args)
        {
            
        }
    }
}
