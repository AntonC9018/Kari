using System;

namespace Kari.Test
{
    public class Hello
    {
        [Command("Hello", "Some parameter")]
        public static string SomeCommand(
            [Argument("pos help")]                    int positional,
            [Argument("optional", "optional help")]   string optional,
            [Option("flag", "idk1", IsFlag = true)]   bool flag,
            [Option("option", "idk2")]                string option = "44")
        {
            return $"{positional}; {optional}; {flag}; {option};";
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var console = new MyConsole();
            console.Commands.Add("Hello", new HelloCommand());

            Console.WriteLine(console.Invoke("Hello world 123 -flag -option=123"));
            Console.WriteLine(console.Invoke("Hello 123 456 -flag -option=789"));
            Console.WriteLine(console.Invoke("Hello 123 world -option=123"));
            Console.WriteLine(console.Invoke("Hello 123 world"));
            Console.WriteLine(console.Invoke("Hello 123 -optional=\"world\""));
            Console.WriteLine(console.Invoke("Hello 123"));
            Console.WriteLine(console.Invoke("Hello"));
            Console.WriteLine(console.Invoke("Hello -help"));
        }
    }
}
