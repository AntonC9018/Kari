using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kari.Arguments;
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
        [Option("The name of the engine-dependent common project.")]
        string engineCommon = "EngineCommon";

        public ProjectEnvironmentData _engineCommon;
        private NamedLogger _logger = new NamedLogger("UnityHelpers");

        public void Initialize() 
        {
            _engineCommon = MasterEnvironment.Instance.AllProjectDatas.FirstOrDefault(p => p.Name == engineCommon);
            if (_engineCommon is null) 
                _logger.LogError($"The engine common project `{engineCommon}` could not be found");
        }
        
        public string GetAnnotations() => "";
        public Task Collect() => Task.CompletedTask;
        public Task Generate()
        {
            _engineCommon.AddCodeFragment(CodeFragment.CreateFromBuilder(
                "Helpers.cs", "UnityHelpers", GenerateCode()));
            return Task.CompletedTask;
        }

        internal CodeBuilder GenerateCode()
        {
            var builder = CodeBuilder.Create();
            builder.AppendLine("namespace ", engineCommon);
            builder.StartBlock();
            builder.AppendLine("using UnityEngine;");
            builder.AppendLine("public static partial class UnityHelpers");
            builder.StartBlock();
            
            foreach (var type in new string[] { "float", "int" })
            {
                foreach (var i in "xyzw") 
                { 
                    var classname = type + i;
                    builder.AppendLine("public struct ", classname);
                    builder.StartBlock();
                    builder.AppendLine($"public readonly {type} value;");
                    builder.AppendLine($"public {classname}({type} value) => this.value = value;");
                    builder.AppendLine($"public static implicit operator {classname}({type} value) => new {classname}(value);");
                    builder.AppendLine($"public static implicit operator {type}({classname} t) => t.value;");
                    builder.EndBlock();
                }
            }

            AppendVectorCode(ref builder, "Vector2", "float",  new string[] { "x", "y" });
            AppendVectorCode(ref builder, "Vector3", "float",  new string[] { "x", "y", "z" });
            AppendVectorCode(ref builder, "Vector4", "float",  new string[] { "x", "y", "z", "w" });
            AppendVectorCode(ref builder, "Vector2Int", "int", new string[] { "x", "y" });
            AppendVectorCode(ref builder, "Vector3Int", "int", new string[] { "x", "y", "z" });
            
            builder.EndBlock();
            builder.EndBlock();

            return builder;
        }

        internal void AppendVectorCode(ref CodeBuilder builder, string vectorName, string type, string[] vars)
        { 
            string[] args = new string[vars.Length];
            List<string> otherParams = new ();
            int bitEnd = 1 << vars.Length;

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
                        otherParams.Add($"{type}{vars[bitIndex]} {vars[bitIndex]}");
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

                builder.AppendLine($"public static {vectorName} With(this in {vectorName} v, {string.Join(", ", otherParams)})");
                builder.StartBlock();
                builder.AppendLine($"return new {vectorName}({callArgs});");
                builder.EndBlock();
                builder.AppendLine();
            }
            builder.AppendLine();
        }
    }
}