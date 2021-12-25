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

    public interface ICollectSymbols
    {
        void CollectSymbols(ProjectEnvironment project);
    }

    public interface IGenerateCode
    {
        // TODO: it makes more sense to pass a wrapper of the stream writer here, 
        // like the CodeBuilder, but which writes to a file since all generators 
        // would always write some sort of code.
        // On the other hand, it's not going to be that much faster, because it would still
        // buffer before flushing, and the locks would get nasty so meh.
        string GenerateCode(ProjectEnvironmentData project);
    }

    public static class AnalyzerMaster
    {
        public static void Initialize<T>(ref T[] slaves) where T : ICollectSymbols, new()
        {
            var projects = MasterEnvironment.Instance.Projects;
            slaves = new T[projects.Count]; 
            for (int i = 0; i < projects.Count; i++)
            {
                slaves[i] = new T();
            }
        }

        public static void Collect<T>(T[] slaves) where T : ICollectSymbols
        {
            var projects = MasterEnvironment.Instance.Projects;
            for (int i = 0; i < slaves.Length; i++)
            {
                slaves[i].CollectSymbols(projects[i]);
            }
        }

        public static Task CollectAsync<T>(T[] slaves) where T : ICollectSymbols
        {
            return Task.Run(() => Collect(slaves));
        }

        public static void Generate<T>(T[] slaves, string fileName)
            where T : IGenerateCode
        {
            var projects = MasterEnvironment.Instance.Projects;
            for (int i = 0; i < slaves.Length; i++)
            {
                // TODO: this does blocking io, fix this.
                projects[i].WriteFile(fileName, slaves[i].GenerateCode(projects[i]));
            }
        }

        public static Task GenerateAsync<T>(T[] slaves, string fileName) 
            where T : IGenerateCode
        {
            return Task.Run(() => Generate(slaves, fileName));
        }
    }
}