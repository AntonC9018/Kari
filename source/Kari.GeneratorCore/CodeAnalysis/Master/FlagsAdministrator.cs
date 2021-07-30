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
            return WhenAllResources<FlagsTemplate>((project, flags) => flags.CollectInfo(project));
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