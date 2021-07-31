using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kari.GeneratorCore;
using Kari.GeneratorCore.CodeAnalysis;

namespace Kari.Plugins.Flags
{

    public class FlagsAdministrator : IAdministrator, IAnalyzerMaster<FlagsAnalyzer>
    {
        public FlagsAnalyzer[] Slaves { get; set; }
        public IGenerator<FlagsAnalyzer> CreateGenerator() => new FlagsTemplate();

        public void Initialize() 
        {
            this.Slaves_Initialize();
            FlagsSymbols.Initialize();
        }
        public Task Collect() => this.Slaves_CollectTask();
        public Task Generate() => this.Slaves_GenerateTask("Flags.cs");
        public IEnumerable<CallbackInfo> GetCallbacks() { yield break; }
    }

}