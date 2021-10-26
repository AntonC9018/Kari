using System.IO;

namespace Kari.GeneratorCore
{
    public static class Stuff
    {
        public static string WithNormalizedDirectorySeparators(this string path)
        {
            return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }
    }
}