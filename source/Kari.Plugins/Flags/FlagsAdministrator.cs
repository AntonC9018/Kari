using System.Threading.Tasks;
using Kari.GeneratorCore.Workflow;
using Kari.Utils;

namespace Kari.Plugins.Flags
{
    public class FlagsAdministrator : IAdministrator
    {
        public FlagsAnalyzer[] _slaves;
        private readonly NamedLogger _logger = new NamedLogger("FlagsPlugin");

        public void Initialize() 
        {
            AdministratorHelpers.Initialize(ref _slaves);
            FlagsSymbols.Initialize(_logger);
        }
        public Task Collect() => AdministratorHelpers.CollectAsync(_slaves);
        public Task Generate() 
        {
            return Task.WhenAll(
                AdministratorHelpers.GenerateAsync(_slaves, "Flags.cs"),
                AdministratorHelpers.AddCodeStringAsync(
                    MasterEnvironment.Instance.CommonPseudoProject,
                    "FlagsAnnotations.cs", nameof(FlagsAnalyzer), DummyFlagsAnnotations.Text)
            );
        }
        public string GetAnnotations() => DummyFlagsAnnotations.Text;
    }

}