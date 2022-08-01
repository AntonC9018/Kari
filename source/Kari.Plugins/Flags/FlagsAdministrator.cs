using System.Threading.Tasks;
using Kari.GeneratorCore.Workflow;
using Kari.Utils;

namespace Kari.Plugins.Flags
{
    public class FlagsAdministrator : IAdministrator
    {
        public FlagsAnalyzer[] _analyzers;
        private readonly NamedLogger _logger = new NamedLogger("FlagsPlugin");

        public void Initialize() 
        {
            AdministratorHelpers.Initialize(ref _analyzers);
        }
        public Task Collect()
        {
            return AdministratorHelpers.CollectAsync(_analyzers);
        }
        public Task Generate() 
        {
            return Task.WhenAll(
                AdministratorHelpers.GenerateAsync(_analyzers, "Flags.cs"),
                AdministratorHelpers.AddCodeStringAsync(
                    MasterEnvironment.Instance.CommonPseudoProject,
                    "FlagsAnnotations.cs", nameof(FlagsAnalyzer), DummyFlagsAnnotations.Text)
            );
        }
        public string GetAnnotations() => DummyFlagsAnnotations.Text;
    }

}