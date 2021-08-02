using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kari.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            var attributeClassRegex = new Regex(@"class\s+([a-zA-Z]+)Attribute\s*:\s*[a-zA-Z.]*Attribute");
            var thing = "class YAttribute : Attribute";
            var m = attributeClassRegex.Matches(thing);
        }
    }
}
