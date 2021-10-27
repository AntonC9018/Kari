using Kari.GeneratorCore.Workflow;

namespace Kari.GeneratorCore
{
    public interface IGenerator<Model> : ICodeTemplate
    {
        /// <summary>
        /// The model, containing all the data necessary to generate code.
        /// </summary>
        Model m { get; set; }
        
        /// <summary>
        /// The namespace of the generated code.
        /// </summary>
        ProjectEnvironmentData Project { get; set; }
    }

    public class CodeGenerator<Model> : CodeTemplateBase, IGenerator<Model>
    {
        public Model m { get; set; }
        public ProjectEnvironmentData Project { get; set; }
    }

    public interface ISimpleGenerator
    {
        string TransformText(ProjectEnvironmentData project);
    }
}