using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Kari.GeneratorCore.CodeAnalysis
{
    public static class AdministratorFinder
    {
        public static readonly List<Assembly> Plugins = new List<Assembly>();

        public static void LoadPlugin(string path)
        {
            var dll = Assembly.LoadFile(path);
            Plugins.Add(dll);
        }

        public static void LoadPluginsDirectory(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                LoadPlugin(file);
            }
        }

        public static void LoadPluginsPaths(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                var extension = Path.GetExtension(paths[i]);
                if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    // if (File.Exists(paths[i]))
                    {
                        LoadPlugin(paths[i]);
                    }
                }
                else
                {
                    LoadPluginsDirectory(paths[i]);
                }
            }
        }

        private static IEnumerable<System.Type> GetAdministratorTypes()
        {
            return Plugins.SelectMany(dll => dll.GetExportedTypes())
                .Where(type => typeof(IAdministrator).IsAssignableFrom(type) && !type.IsAbstract);
        }

        /// <summary>
        /// Adds the administrators specified by name in `namesToAdd`, removing these names from there.
        /// The names must be in the correct case (exactly match the class names).
        /// </summary>
        public static bool AddAdministrators(this MasterEnvironment environment, HashSet<string> namesToAdd)
        {
            foreach (var adminType in GetAdministratorTypes())
            {
                if (namesToAdd.Remove(adminType.Name) || namesToAdd.Remove(adminType.Name.Replace("Administrator", "")))
                {
                    var admin = (IAdministrator) System.Activator.CreateInstance(adminType);
                    environment.Administrators.Add(admin);
                }
            }

            if (namesToAdd.Count > 0)
            {
                foreach (var name in namesToAdd)
                {
                    System.Console.WriteLine($"Invalid administrator name: {name}");
                }
                return false;
            }

            return true;
        }

        public static void AddAllAdministrators(this MasterEnvironment environment)
        {
            foreach (var adminType in GetAdministratorTypes())
            {
                var admin = (IAdministrator) System.Activator.CreateInstance(adminType);
                environment.Administrators.Add(admin);
            }
        }
    }
}