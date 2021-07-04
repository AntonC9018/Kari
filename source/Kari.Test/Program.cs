using System;
using Kari.Shared;

namespace Kari.Test
{
    [KariTestAttribute("Hello")]
    public class Hello{}

    class Program
    {
        static void Main(string[] args)
        {
            foreach (var type in typeof(Program).Assembly.GetTypes())
                Console.WriteLine(type.FullName);
        }
    }
}
