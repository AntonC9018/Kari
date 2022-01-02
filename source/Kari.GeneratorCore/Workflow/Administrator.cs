using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kari.Utils;

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
        /// <summary>
        /// Used to output the code of a given code generator (analyzer/template)
        /// </summary>
        void GenerateCode(ProjectEnvironmentData project, ref CodeBuilder codeBuilder);
    }

    public static class AdministratorHelpers
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
            return Task.Run(delegate { Collect(slaves); });
        }

        public static void Generate<T>(T[] slaves, string fileName)
            where T : IGenerateCode
        {
            var projects = MasterEnvironment.Instance.Projects;
            var fragment = new CodeFragment
            {
                FileNameHint = fileName,
                NameHint = typeof(T).Name,
                CodeBuilder = CodeBuilder.Create(),
            };
            fragment.CodeBuilder.Append(CodeFileCommon.HeaderBytes);
            for (int i = 0; i < slaves.Length; i++)
                slaves[i].GenerateCode(projects[i], ref fragment.CodeBuilder);
            fragment.CodeBuilder.Append(CodeFileCommon.FooterBytes);
        }

        public static Task GenerateAsync<T>(T[] slaves, string fileName) 
            where T : IGenerateCode
        {
            return Task.Run(delegate { Generate(slaves, fileName); });
        }

        public static void AddCodeString(ProjectEnvironmentData project, string fileName, string nameHint, string content)
        {
            var builder = CodeBuilder.Create();
            builder.Append(CodeFileCommon.HeaderBytes);
            builder.Append(content);
            builder.Append(CodeFileCommon.FooterBytes);

            project.AddCodeFragment(new CodeFragment
            {
                FileNameHint = fileName,
                NameHint = nameHint,
                CodeBuilder = builder,
            }); 
        }

        public static Task AddCodeStringAsync(ProjectEnvironmentData project, string fileName, string nameHint, string content)
        {
            return Task.Run(delegate { AddCodeString(project, fileName, nameHint, content); });
        }

    }
}