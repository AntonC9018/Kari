using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kari.Utils;

namespace Kari.GeneratorCore.Workflow
{
    /// <summary>
    /// Such a thing.
    /// </summary>
    public struct CodeFragment : IComparable<CodeFragment>
    {
        /// <summary>
        /// When the code is written to a file, this indicates the name of the file.
        /// The code is not guaranteed to be written into this specific file though.
        /// If another code fragment had the same name, the actual file name 
        /// is going to be appended `_NameHint` to.
        /// </summary>
        public string FileNameHint { get; init; }

        /// <summary>
        /// The identification name of the entity that has produced the content.
        /// Using the class or the plugin name is ok.
        /// This may be used to disambiguate files with the same `FileNameHint`.
        /// </summary>
        public string NameHint { get; init; }

        /// <summary>
        /// UTF8 encoded byte array of characters.
        /// </summary>
        public ArraySegment<byte> Bytes { get; init; }

        /// <summary>
        /// </summary>
        public bool AreBytesRentedFromArrayPool { get; init; }

        /// <summary>
        /// </summary>
        public static CodeFragment CreateFromBuilder(string fileNameHint, string nameHint, CodeBuilder builder)
        {
            return new CodeFragment
            {
                FileNameHint = fileNameHint,
                NameHint = nameHint,
                Bytes = builder.AsArraySegment(),
                AreBytesRentedFromArrayPool = true,
            };
        }

        /// <summary>
        /// Orders fragments by name.
        /// </summary>
        public int CompareTo(CodeFragment other)
        {
            int file = FileNameHint.CompareTo(other.FileNameHint);
            if (file != 0)
                return file;

            return NameHint.CompareTo(other.NameHint);
        }
        
        /// <summary>
        /// Function used to produce a disambiguated name.
        /// </summary>
        public string GetLongName()
        {
            return FileNameHint + "__" + NameHint;
        }

        // I used this to see the bytes as text in the debugger
        #if DEBUG
            public string BytesAsString => Encoding.UTF8.GetString(Bytes);
        #endif
    }

    /// <summary>
    /// Data common to all writers, such as the header and the footer of the generated files.
    /// </summary>
    public static class CodeFileCommon
    {
        public const string HeaderString = @"// <auto-generated>
// This file has been autogenerated by Kari.
// </auto-generated>

#pragma warning disable

";
        public static readonly byte[] HeaderBytes = Encoding.UTF8.GetBytes(HeaderString); 

        public const string FooterString = "\r\n#pragma warning restore\r\n";
        public static readonly byte[] FooterBytes = Encoding.UTF8.GetBytes(FooterString); 

        public static void InitializeGeneratedDirectory(string directory)
        {
            Directory.CreateDirectory(directory);
            var gitignore = Path.Join(directory, ".gitignore");
            if (!File.Exists(gitignore) && !Directory.Exists(gitignore))
            {
                File.WriteAllText(gitignore, "*\r\n!.gitignore");
            }
        }

        public static readonly byte[] SlashesSpaceBytes = Encoding.UTF8.GetBytes("// ");
        public static readonly byte[] NewLineBytes = Encoding.UTF8.GetBytes(Environment.NewLine);
        public static readonly byte[] SpaceBytes = Encoding.UTF8.GetBytes(" ");
    }
}