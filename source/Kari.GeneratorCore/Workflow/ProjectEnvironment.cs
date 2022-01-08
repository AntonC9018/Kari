using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Kari.Utils;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore.Workflow
{
    /// <summary>
    /// aka a pseudoproject. 
    /// Holds metadata about a project.
    /// </summary>
    public record class ProjectEnvironmentData
    {
        /// <summary>
        /// </summary>
        public NamedLogger Logger { get; init; }

        /// <summary>
        /// This does not necessarily correspond to the namespace name.
        /// In general, a project needs not have a concrete namespace.
        /// The generated namespace though is a known thing, see `GeneratedNamespaceName`.
        /// </summary>
        public string Name => Logger.Name;

        /// <summary>
        /// Directory with the source files, including the source code and the project files.
        /// </summary>
        public string DirectoryFullPath { get; init; }
        
        /// <summary>
        /// The name of the namespace that the generated code will end up in.
        /// It is most likely going to be Name.Generated, but it depends on the configuration.
        /// </summary>
        public string GeneratedNamespaceName { get; init; }

        public readonly List<CodeFragment> CodeFragments = new();

        public ProjectEnvironmentData(string projectName, string directoryFullPath, string generatedNamespaceName)
        {
            Logger = new NamedLogger(projectName);
            DirectoryFullPath = directoryFullPath;
            GeneratedNamespaceName = generatedNamespaceName;
        }


        /// <summary>
        /// Writes the text to a file with the given file name, 
        /// placed in the directory of this project, with the current /Generated suffix appended to it.
        /// </summary>
        public void AddCodeFragment(CodeFragment fragment)
        {
            lock (CodeFragments)
            {
                CodeFragments.Add(fragment);
            }
        }

        /// <summary>
        /// Is not thread safe.
        /// </summary>
        public void DisposeOfCodeFragments()
        {
            foreach (ref var f in CollectionsMarshal.AsSpan(CodeFragments))
            {
                if (f.AreBytesRentedFromArrayPool)
                    ArrayPool<byte>.Shared.Return(f.Bytes.Array);
            }
            CodeFragments.Clear();
        }
    }

    /// <summary>
    /// Stores cached symbols for a project.
    /// </summary>
    public record class ProjectEnvironment(
        ProjectEnvironmentData Data, 
        SyntaxTree[] SourceFilesSyntaxTrees,
        INamedTypeSymbol[] Types)
    {
        public NamedLogger Logger => Data.Logger;

        public IEnumerable<INamedTypeSymbol> TypesWithAttributes
        {
            get
            {
                return Types.Where(t => t.GetAttributes().Length > 0);
            }
        }

        
        public IEnumerable<IMethodSymbol> MethodsWithAttributes
        {
            get
            {
                return Types
                    .SelectMany(t => t.GetMembers())
                    .OfType<IMethodSymbol>()
                    .Where(t => t.GetAttributes().Length > 0);
            }
        }
    }
}
