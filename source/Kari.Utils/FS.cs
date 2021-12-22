using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace Kari.Utils
{
    public static class FileSystem
    {
        public static IEnumerable<string> EnumerateDirectoriesIgnoringSingleDirectory(
            string rootDirectory, string ignoredDirectoryFullPath, string searchPattern = "*")
        {
            Debug.Assert(rootDirectory != null, "Check yourself before calling");
            Debug.Assert(ignoredDirectoryFullPath != null, "Check yourself before calling");
            Debug.Assert(searchPattern != null, "Invalid pattern");

            Stack<string> directories = new Stack<string>();
            directories.Push(rootDirectory);
            while (directories.Count > 0)
            {
                string current = directories.Pop();
                if (current.EndsWith(ignoredDirectoryFullPath))
                    break;
                foreach (var subdir in Directory.EnumerateDirectories(current, searchPattern, SearchOption.TopDirectoryOnly))
                {
                    directories.Push(subdir);
                    yield return subdir;
                }
            }
            while (directories.Count > 0)
            {
                foreach (var subdir in Directory.EnumerateDirectories(directories.Pop(), searchPattern, SearchOption.AllDirectories))
                    yield return subdir;
            }
        }

        public static IEnumerable<string> EnumerateFilesIgnoringSingleDirectory(
            string rootDirectory, string ignoredDirectoryFullPath, string fileSearchPattern = "*")
        {
            Debug.Assert(rootDirectory != null, "Check yourself before calling");
            Debug.Assert(ignoredDirectoryFullPath != null, "Check yourself before calling");
            Debug.Assert(fileSearchPattern != null, "Invalid pattern");

            Stack<string> directories = new Stack<string>();
            directories.Push(rootDirectory);
            while (directories.Count > 0)
            {
                string current = directories.Pop();
                if (current.EndsWith(ignoredDirectoryFullPath))
                    break;
                foreach (var subdir in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
                    directories.Push(subdir);
                foreach (var file in Directory.EnumerateFiles(current, fileSearchPattern, SearchOption.TopDirectoryOnly))
                    yield return file;
            }
            while (directories.Count > 0)
            {
                foreach (var file in Directory.EnumerateFiles(directories.Pop(), fileSearchPattern, SearchOption.AllDirectories))
                    yield return file;
            }
        }
        
        public interface IShouldIgnoreDirectory
        {
            bool ShouldIgnoreDirectory(string fullFilePath);
        }

        public static IEnumerable<string> EnumerateFilesIgnoring(
            [NotNull] string rootDirectory, 
            [NotNull] IShouldIgnoreDirectory ignore,
            [NotNull] string fileSearchPattern = "*")
        {
            Debug.Assert(rootDirectory != null, "Check yourself before calling");
            Debug.Assert(ignore != null, "Check yourself before calling");
            Debug.Assert(fileSearchPattern != null, "Invalid pattern");

            Stack<string> directories = new Stack<string>();
            directories.Push(rootDirectory);
            while (directories.Count > 0)
            {
                string current = directories.Pop();
                if (ignore.ShouldIgnoreDirectory(current))
                    continue;
                foreach (var subdir in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
                    directories.Push(subdir);
                foreach (var file in Directory.EnumerateFiles(current, fileSearchPattern, SearchOption.TopDirectoryOnly))
                    yield return file;
            }
        }

        public static string WithNormalizedDirectorySeparators(this string path)
        {
            return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }

        public static string ToFullNormalizedPath(this string path)
        {
            path = FileSystem.WithNormalizedDirectorySeparators(path);
            return Path.GetFullPath(path);
        }
    }
}