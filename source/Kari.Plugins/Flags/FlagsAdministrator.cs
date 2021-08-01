using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Kari.GeneratorCore;
using Kari.GeneratorCore.Workflow;

namespace Kari.Plugins.Flags
{
    using System.Text.RegularExpressions;

    public class FlagsAdministrator : IAdministrator
    {
        public FlagsAnalyzer[] _slaves;

        public void Initialize() 
        {
            AnalyzerMaster.Initialize(ref _slaves);
            FlagsSymbols.Initialize();
        }
        public Task Collect() => AnalyzerMaster.CollectTask(_slaves);
        public Task Generate() => AnalyzerMaster.GenerateTask(_slaves, new FlagsTemplate(), "Flags.cs");
        public IEnumerable<CallbackInfo> GetCallbacks() { yield break; }
        public string GetAnnotations() => DummyFlagsAnnotations.Text;
    }

}