using System;
using System.Collections.Generic;
using Kari.GeneratorCore;
using Kari.GeneratorCore.Workflow;
using Kari.Utils;
using Microsoft.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Kari.Plugins.Flags
{
    public struct FlagsInfo
    {
        public FlagsInfo(INamedTypeSymbol symbol)
        {
            Name = symbol.Name;
            FullName = symbol.GetFullyQualifiedName();
        }

        public readonly string Name;
        public readonly string FullName;
    }

    public partial class FlagsAnalyzer : ICollectSymbols, IGenerateCode
    {
        public readonly List<FlagsInfo> _infos = new List<FlagsInfo>();

        public void CollectSymbols(ProjectEnvironment environment)
        {
            // It should be able to crank through those symbols fast on its own, so this
            // Task.Run is debatable.
            foreach (var t in environment.TypesWithAttributes)
            {
                if (t.HasAttribute(FlagsSymbols.NiceFlagsAttribute.symbol))
                {
                    _infos.Add(new FlagsInfo(t));
                }
            }
        }

        // Doing FormattableString like here 
        // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/interpolated#implicit-conversions-and-how-to-specify-iformatprovider-implementation
        // is annoying for code specifically, because all of the { will have to be escaped.
        const string template = @"public static class $(Name)FlagsExtensions
    {
        /// <summary>
        /// Checks whether the given flags intersect with the other flags.
        /// Returns true if either of the other flags are set on the flags.
        /// To see if flags contain all of some other flags, use <c>HasFlag()</c> instead. 
        /// </summary>
        public static bool HasEitherFlag(this $(FullName) flag1, $(FullName) flag2)
        {
            return (flag1 & flag2) != 0;
        }

        /// <summary>
        /// Checks whether the given flags does not intersect with the other flags.
        /// Returns false if either of the other flags are set on the flags.
        /// This function does the same as negating a call to <c>HasEitherFlag()</c>.
        /// </summary>
        public static bool HasNeitherFlag(this $(FullName) flag1, $(FullName) flag2)
        {
            return (flag1 & flag2) == 0;
        }

        /// <summary>
        /// Modifies the given <c>$(Name)</c>, setting the given flags.
        /// </summary>
        public static ref $(FullName) Set(ref this $(FullName) flagInitial, $(FullName) flagToSet)
        {
            flagInitial = flagInitial | flagToSet;
            return ref flagInitial;
        }

        /// <summary>
        /// Returns a new <c>$(Name)</c> with the given flags set.
        /// </summary>
        public static $(FullName) WithSet(this $(FullName) flagInitial, $(FullName) flagToSet)
        {
            return flagInitial | flagToSet;
        }
        
        /// <summary>
        /// Modifies the given <c>$(Name)</c> unsetting the given flags.
        /// </summary>
        public static ref $(FullName) Unset(ref this $(FullName) flagInitial, $(FullName) flagToSet)
        {
            flagInitial = flagInitial & (~flagToSet);
            return ref flagInitial;
        }

        /// <summary>
        /// Returns a new <c>$(Name)</c> with the given flags unset.
        /// </summary>
        public static $(FullName) WithUnset(this $(FullName) flagInitial, $(FullName) flagToSet)
        {
            return flagInitial & (~flagToSet);
        }
        
        /// <summary>
        /// Modifies the given <c>$(Name)</c> with the given flags set or unset, 
        /// indicated by the <c>set</c> boolean parameter.
        /// </summary>
        public static ref $(FullName) Set(ref this $(FullName) flagInitial, $(FullName) flagToSet, bool set)
        {
            if (set) flagInitial = flagInitial | flagToSet;
            else     flagInitial = flagInitial & (~flagToSet);
            return ref flagInitial;
        }

        /// <summary>
        /// Returns a new <c>$(Name)</c> with the given flags set or unset, 
        /// indicated by the <c>set</c> boolean parameter.
        /// </summary>
        public static $(FullName) WithSet(this $(FullName) flagInitial, $(FullName) flagToSet, bool set)
        {
            if (set) return flagInitial | flagToSet;
            else     return flagInitial & (~flagToSet);
        }
    }
    ";

        private void AppendCodeForSingleInfo(in FlagsInfo info, ref CodeBuilder builder)
        {
            builder.Indent();
            builder.FormattedAppend(template, 
                "Name", info.Name, 
                "FullName", info.FullName);
            builder.NewLine();
        }

        public void GenerateCode(ProjectEnvironmentData project, ref CodeBuilder builder)
        {
            if (_infos.Count == 0)
                return;

            builder.AppendLine("namespace ", project.GeneratedNamespaceName);
            builder.StartBlock();
            builder.AppendLine("using System.Collections.Generic;");
            foreach (ref var info in CollectionsMarshal.AsSpan(_infos)) 
                AppendCodeForSingleInfo(in info, ref builder);
            builder.EndBlock();
        }
    }
}
