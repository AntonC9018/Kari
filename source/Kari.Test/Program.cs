using System;
using Kari;

namespace Kari.Test
{
    public class Hello
    {
        [Command("Hello", "Some parameter")]
        public static string SomeCommand(
            [Argument("positional")]                int positional,
            [Argument("optional", "positional")]    string optional,
            [Option("flag", "idk", IsFlag = true)]  bool flag,
            [Option("option", "idk")]               string option)
        {
            return $"{positional}; {optional}; {flag}; {option};";
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            foreach (var type in typeof(Program).Assembly.GetTypes())
                Console.WriteLine(type.FullName);
        }
    }
}
