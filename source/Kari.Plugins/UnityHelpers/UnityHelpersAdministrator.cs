using System.Threading.Tasks;
using Kari.GeneratorCore;
using Kari.GeneratorCore.Workflow;
using Kari.Utils;

namespace Kari.Plugins.UnityHelpers
{
    /// <summary>
    /// This is a analyzer-less plugin, that is, it generates the same output independent of the input project.
    /// </summary>
    public class UnityHelpersAdministrator : IAdministrator
    {
        [Option("The namespace of the engine-dependent common project")]
        string engineCommon = "EngineCommon";

        public ProjectEnvironmentData _engineCommon;
        private Logger _logger = new Logger("UnityHelpers admin");

        public void Initialize() 
        {
            _engineCommon = MasterEnvironment.Instance.Projects.Find(p => p.NamespaceName == engineCommon);
            if (_engineCommon is null) 
                _logger.LogError($"The engine common project `{engineCommon}` could not be found");
        }
        
        public string GetAnnotations() => "";
        public Task Collect() => Task.CompletedTask;
        public Task Generate()
        {
            return _engineCommon.WriteFileAsync("Helpers.cs", new HelpersTemplate());
        }
    }

    public partial class HelpersTemplate
    {
        private string DoVector(string vectorName, string type, string[] vars, string initialIndentation = "        ")
        { 
            string[] args = new string[vars.Length];
            var otherParams = new ListBuilder(", ");
            int bitEnd = 1 << vars.Length;
            var codeBuilder = new CodeBuilder("    ", initialIndentation);

            for (int bitCombo = 1; bitCombo < bitEnd - 1; bitCombo++) 
            { 
                otherParams.Clear();
                for (int bitIndex = 0; bitIndex < vars.Length; bitIndex++)
                {
                    int t = bitCombo >> bitIndex;
                    
                    if ((t & 1) == 0)
                    {
                        // The index is not in this combo: fill in with the default 
                        args[bitIndex] = "v." + vars[bitIndex];
                    }
                    else
                    {
                        otherParams.Append($"{type}{vars[bitIndex]} {vars[bitIndex]}");
                        // Fill in with the variable name
                        args[bitIndex] = vars[bitIndex];
                    }
                }
                var callArgs = string.Join(", ", args);

                // Does not make sense. You can just set these directly
                // codeBuilder.AppendLine($"public static void Set(this ref {vectorName} v, {otherParams})");
                // codeBuilder.StartBlock();
                // codeBuilder.AppendLine($"v = new {vectorName}({callArgs});");
                // codeBuilder.EndBlock();
                // codeBuilder.AppendLine();

                codeBuilder.AppendLine($"public static {vectorName} With(this in {vectorName} v, {otherParams})");
                codeBuilder.StartBlock();
                codeBuilder.AppendLine($"return new {vectorName}({callArgs});");
                codeBuilder.EndBlock();
                codeBuilder.AppendLine();
            }

            return codeBuilder.ToString();
        } 
    }
}