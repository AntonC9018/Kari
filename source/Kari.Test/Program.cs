using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Test
{
    class Program
    {
        public readonly struct A
        {
            public int s { get; }
            public A(int s)
            {
                this.s = s;
            }
        } 
        static void Main(string[] args)
        {
            int k = 9;
            int add(int x, int y) => x + y + k;
            int num_times = 100000000;
            var s = Stopwatch.StartNew();
            int sum = 0;
            for (int i = 0, j = 0; i < num_times; i++, j++)
            {
                k = i;
                sum = add(i, j);
            } 
            s.Stop();
            System.Console.WriteLine(s.Elapsed);

            s.Reset();
            s.Start();
            int g = 0;
            for (int i = 0, j = 0; i < num_times; i++, j++)
            {
                k = i;
                g = i + j + k;
            }
            s.Stop();
            System.Console.WriteLine(s.Elapsed);  
            System.Console.WriteLine(sum + g);      
        }
    }
}
