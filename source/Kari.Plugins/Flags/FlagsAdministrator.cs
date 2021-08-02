using System.Threading.Tasks;
using Kari.GeneratorCore.Workflow;

namespace Kari.Plugins.Flags
{
    public class FlagsAdministrator : IAdministrator
    {
        public FlagsAnalyzer[] _slaves;

        public void Initialize() 
        {
            AnalyzerMaster.Initialize(ref _slaves);
            FlagsSymbols.Initialize();
        }
        public Task Collect() => AnalyzerMaster.CollectAsync(_slaves);
        public Task Generate() 
        {
            return Task.WhenAll(
                AnalyzerMaster.GenerateAsync(_slaves, "Flags.cs", new FlagsTemplate()),
                MasterEnvironment.Instance.CommonPseudoProject.WriteFileAsync("FlagsAnnotations.cs", DummyFlagsAnnotations.Text)
            );
        }
        public string GetAnnotations() => DummyFlagsAnnotations.Text;
    }

}