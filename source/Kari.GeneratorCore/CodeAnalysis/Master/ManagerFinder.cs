using System.Collections.Generic;
using System.Linq;

namespace Kari.GeneratorCore.CodeAnalysis
{
    public static class AdministratorFinder
    {
        public static IEnumerable<System.Type> GetAdministratorTypes()
        {
            return typeof(AdministratorFinder).Assembly.GetTypes()
                .Where(type => typeof(AdministratorBase).IsAssignableFrom(type));
        }

        public static bool AddAdministrators(this MasterEnvironment environment, HashSet<string> namesToAdd)
        {
            foreach (var adminType in GetAdministratorTypes())
            {
                if (namesToAdd.Remove(adminType.Name))
                {
                    var admin = (AdministratorBase) System.Activator.CreateInstance(adminType);
                    environment.Administrators.Raw.Add(adminType, admin);
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
                var admin = (AdministratorBase) System.Activator.CreateInstance(adminType);
                environment.Administrators.Raw.Add(adminType, admin);
            }
        }
    }
}