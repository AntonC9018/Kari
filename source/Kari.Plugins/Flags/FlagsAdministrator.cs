using System.Threading.Tasks;
using Kari.GeneratorCore.Workflow;
using Kari.Utils;

namespace Kari.Plugins.Flags
{
    public class FlagsAdministrator : IAdministrator
    {
        public FlagsAnalyzer[] _slaves;
        private readonly Logger _logger = new Logger("FlagsPlugin");

        public void Initialize() 
        {
            AnalyzerMaster.Initialize(ref _slaves);
            FlagsSymbols.Initialize(_logger);
        }
        public Task Collect() => AnalyzerMaster.CollectAsync(_slaves);
        public Task Generate() 
        {
            return Task.WhenAll(
                AnalyzerMaster.GenerateAsync(_slaves, "Flags.cs"),
                MasterEnvironment.Instance.CommonPseudoProject.WriteFileAsync("FlagsAnnotations.cs", DummyFlagsAnnotations.Text)
            );
        }
        public string GetAnnotations() => DummyFlagsAnnotations.Text;
    }

}