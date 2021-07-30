using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kari.GeneratorCore.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore
{
    public class FlagsAdministrator : AdministratorBase
    {
        public override void Initialize()
        {
            AddResourceToAllProjects<FlagsTemplate>();
        }
        public override Task Collect()
        {
            var tasks = _masterEnvironment.Projects.Select(
                project => project.Resources.Get<FlagsTemplate>().CollectInfo(project));
            return Task.WhenAll(tasks);
        }

        public override Task Generate()
        {
            return WriteFilesTask<FlagsTemplate>("Flags.cs");
        }

        public override IEnumerable<CallbackInfo> GetCallbacks()
        {
            yield break;
        }
    }

}