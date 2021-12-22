using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Kari.GeneratorCore.Workflow
{
    public static class AdministratorFinder
    {
        public static readonly List<Assembly> Plugins = new List<Assembly>();

        public static void LoadPlugin(string path)
        {
            var dll = Assembly.LoadFile(path);
            Plugins.Add(dll);
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
        public static void AddAdministrators(MasterEnvironment environment, HashSet<string> namesToAdd)
        {
            foreach (var adminType in GetAdministratorTypes())
            {
                if (namesToAdd.Remove(adminType.Name) || namesToAdd.Remove(adminType.Name.Replace("Administrator", "")))
                {
                    var admin = (IAdministrator) System.Activator.CreateInstance(adminType);
                    environment.Administrators.Add(admin);
                }
            }
        }

        public static void AddAllAdministrators(MasterEnvironment environment)
        {
            foreach (var adminType in GetAdministratorTypes())
            {
                var admin = (IAdministrator) System.Activator.CreateInstance(adminType);
                environment.Administrators.Add(admin);
            }
        }
    }
}