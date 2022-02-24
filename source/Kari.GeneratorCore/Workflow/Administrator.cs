using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Text;
using Kari.Utils;
using static System.Diagnostics.Debug;

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
        IEnumerable<MasterEnvironment.CallbackInfo> GetCallbacks() { yield break; }

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
        public static void Initialize<T>(ref T[] collectors) where T : ICollectSymbols, new()
        {
            var projects = MasterEnvironment.Instance.Projects;
            collectors = new T[projects.Length]; 
            for (int i = 0; i < projects.Length; i++)
            {
                collectors[i] = new T();
            }
        }

        public static void Collect<T>(T[] collectors) where T : ICollectSymbols
        {
            var projects = MasterEnvironment.Instance.Projects;
            for (int i = 0; i < collectors.Length; i++)
            {
                collectors[i].CollectSymbols(projects[i]);
            }
        }

        public static Task CollectAsync<T>(T[] collectors) where T : ICollectSymbols
        {
            return Task.Run(delegate { Collect(collectors); });
        }

        public static void Generate<T>(T[] generators, string fileName)
            where T : IGenerateCode
        {
            var projects = MasterEnvironment.Instance.Projects;
            
            for (int i = 0; i < generators.Length; i++)
            {
                var builder = CodeBuilder.Create();

                generators[i].GenerateCode(projects[i].Data, ref builder);

                // MaybeAppendTrailingNewLine(builder);
                if (builder.StringBuilder.Length > 0)
                {
                    projects[i].Data.AddCodeFragment(new CodeFragment
                    {
                        FileNameHint = fileName,
                        NameHint = typeof(T).Name,
                        Bytes = builder.AsArraySegment(),
                        AreBytesRentedFromArrayPool = true,
                    });
                }
            }
        }

        // NOTE: We always do \r\n not \n
        // private static void MaybeAppendTrailingNewLine(CodeBuilder builder)
        // {
        //     var length = builder.StringBuilder.Length;
        //     // definitely no trailing new line
        //     if (length < 2)
        //         builder.NewLine();

        //     var lastChars = builder.StringBuilder.AsSpan()[(length - 2) .. length];

        //     // It's utf8, we know it's fine to check like this
        //     // We know \n must be preceeded by an \r (see the comment in CodeBuilder.NewLine()).
        //     if (lastChars[1] == '\n')
        //     {
        //         Assert(lastChars[0] == '\r');
        //     }
        //     else
        //     {
        //         builder.NewLine();
        //     }
        // }

        public static Task GenerateAsync<T>(T[] slaves, string fileName) 
            where T : IGenerateCode
        {
            return Task.Run(delegate { Generate(slaves, fileName); });
        }

        public static void AddCodeString(ProjectEnvironmentData project, string fileName, string nameHint, string content)
        {
            project.AddCodeFragment(new CodeFragment
            {
                FileNameHint = fileName,
                NameHint = nameHint,
                Bytes = Encoding.UTF8.GetBytes(content),
                AreBytesRentedFromArrayPool = false,
            });
        }

        public static Task AddCodeStringAsync(ProjectEnvironmentData project, string fileName, string nameHint, string content)
        {
            return Task.Run(delegate { AddCodeString(project, fileName, nameHint, content); });
        }

    }
}