using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Kari.GeneratorCore.Workflow
{
    // Just a helper thing to load plugins, probably exists in this form just temporarily
    public class AdministratorFinder
    {
        private List<Assembly> _plugins = new();

        public void LoadPlugin(string path)
        {
            var dll = Assembly.LoadFile(path);
            _plugins.Add(dll);
        }

        private IEnumerable<System.Type> GetAdministratorTypes()
        {
            return _plugins.SelectMany(dll => dll.GetExportedTypes())
                .Where(type => typeof(IAdministrator).IsAssignableFrom(type) && !type.IsAbstract);
        }

        /// <summary>
        /// Adds the administrators specified by name in `namesToAdd`, removing these names from there.
        /// The names must be in the correct case (exactly match the class names).
        /// </summary>
        public void AddAdministrators(MasterEnvironment environment, HashSet<string> namesToAdd)
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

        public void AddAllAdministrators(MasterEnvironment environment)
        {
            foreach (var adminType in GetAdministratorTypes())
            {
                var admin = (IAdministrator) System.Activator.CreateInstance(adminType);
                environment.Administrators.Add(admin);
            }
        }
    }
}