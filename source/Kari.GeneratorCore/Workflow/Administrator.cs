using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kari.GeneratorCore.Workflow
{
    public interface IAdministrator
    {
        /// <summary>
        /// Get the content of the file with annotations associated with the given administrator.
        /// This information will be used to update the existing compilation.
        /// </summary>
        string GetAnnotations();

        /// <summary>
        /// Returns the object to take arguments from the command line.
        /// It is called before initialization.
        /// You should do validation of received values in the `Initialize()` method.
        /// </summary>
        object GetArgumentObject() => this;

        /// <summary>
        /// Method called after a reference to MasterEnvironment has been set.
        /// The MasterEnvironment already contains the projects at this point.
        /// </summary>
        void Initialize();

        /// <summary>
        /// The method called asynchronously by the MasterEnvironment to initiate the symbol collection process.
        /// You must initiate the symbol collecting processes of the resources you control.
        /// </summary>
        Task Collect();

        /// <summary>
        /// The method through which the MasterEnvironment figures out the dependencies between the different
        /// resources and administrators. This function must return the callbacks with the associated
        /// priority numbers. The lower the priority, the sooner the callback will execute.
        /// By knowing what priority number a different Administrator will use, you can run your handler
        /// right before or right after theirs, by setting the priority of your handler to a lower or to 
        /// a higher value respectively.
        /// </summary>
        IEnumerable<CallbackInfo> GetCallbacks() { yield break; }

        /// <summary>
        /// The method called asynchronously by the MasterEnvironment to initiate the code generation process.
        /// </summary>
        Task Generate();
    }

    public interface IAnalyzer
    {
        void Collect(ProjectEnvironment project);
    }

    public static class AnalyzerMaster
    {
        public static void Initialize<T>(ref T[] slaves) where T : IAnalyzer, new()
        {
            var projects = MasterEnvironment.Instance.Projects;
            slaves = new T[projects.Count]; 
            for (int i = 0; i < projects.Count; i++)
            {
                slaves[i] = new T();
            }
        }

        public static void Collect<T>(T[] slaves) where T : IAnalyzer
        {
            var projects = MasterEnvironment.Instance.Projects;
            for (int i = 0; i < slaves.Length; i++)
            {
                slaves[i].Collect(projects[i]);
            }
        }

        public static Task CollectAsync<T>(T[] slaves) where T : IAnalyzer
        {
            return Task.Run(() => Collect(slaves));
        }

        public static void Generate<T>(T[] slaves, string fileName, IGenerator<T> generator)
            where T : IAnalyzer
        {
            var projects = MasterEnvironment.Instance.Projects;
            for (int i = 0; i < slaves.Length; i++)
            {
                generator.m = slaves[i];
                generator.Project = projects[i];
                projects[i].WriteFile(fileName, generator.TransformText());
            }
        }

        public static Task GenerateAsync<T>(T[] slaves, string fileName, IGenerator<T> generator) 
            where T : IAnalyzer
        {
            return Task.Run(() => Generate(slaves, fileName, generator));
        }

        public static void Generate<T>(T[] slaves, string fileName)
            where T : ITransformText
        {
            var projects = MasterEnvironment.Instance.Projects;
            for (int i = 0; i < slaves.Length; i++)
            {
                projects[i].WriteFile(fileName, slaves[i].TransformText(projects[i]));
            }
        }

        public static Task GenerateAsync<T>(T[] slaves, string fileName) 
            where T : ITransformText
        {
            return Task.Run(() => Generate(slaves, fileName));
        }
    }
}