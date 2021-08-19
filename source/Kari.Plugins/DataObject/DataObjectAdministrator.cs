// Template file generated by Baton. Feel free to change it or remove this message.
using System.Threading.Tasks;
using Kari.GeneratorCore.Workflow;

namespace Kari.Plugins.DataObject
{
    // The plugin interface to Kari.
    // Kari will call methods of this class to analyze and then generate code.
    public class DataObjectAdministrator : IAdministrator
    {
        public DataObjectAnalyzer[] _analyzers;
        
        public void Initialize()
        {
            AnalyzerMaster.Initialize(ref _analyzers);

            var logger = new Logger("DataObject Plugin");
            DataObjectSymbols.Initialize(logger);
        }
        
        public Task Collect()
        {
            return AnalyzerMaster.CollectAsync(_analyzers);
        }

        public Task Generate()
        {
            return Task.WhenAll(
                AnalyzerMaster.GenerateAsync(_analyzers, "DataObjects.cs", new DataObjectTemplate()),
                MasterEnvironment.Instance.CommonPseudoProject.WriteFileAsync("DataObjectAnnotations.cs", GetAnnotations())
            );
        }
        
        public string GetAnnotations() => DummyDataObjectAnnotations.Text;
    }
}
