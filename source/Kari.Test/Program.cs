using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kari.Test
{
    class Program
    {
        public static async Task Stuff()
        {
            Thread.Sleep(1000);
            Console.WriteLine("Delay");
        }

        static async Task Main(string[] args)
        {
            var t = Stuff();
            Console.WriteLine("After");
            await t;
        }
    }
}
