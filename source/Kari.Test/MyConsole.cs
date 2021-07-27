using System;
using System.Collections.Generic;

namespace Kari.Test
{
    public class MyConsole
    {
        public Dictionary<string, CommandBase> Commands { get; }

        public MyConsole()
        {
            Commands = new Dictionary<string, CommandBase>();
        }

        public static int GetIndexOfWhitespace(string str)
        {
            int i = 0;
            while (i < str.Length || !char.IsWhiteSpace(str[i]))
            {
                i++;
            }
            return i;
        }

        public string Invoke(string inputCommand)
        {
            var ctx = new CommandContext(inputCommand);

            ctx.Parser.SkipWhitespace();
            var commandName = ctx.Parser.GetCommandName();

            if (commandName == null)
            {
                return "";
            }

            ctx.Parser.SkipWhitespace();

            if (Commands.TryGetValue(commandName, out var command))
            {
                try
                {
                    return command.Execute(ctx);
                }
                catch (Exception exc)
                {
                    return exc.Message;
                }
            }
            else
            {
                return $"Unknown command: {commandName}";
            }
        }
    }
}
