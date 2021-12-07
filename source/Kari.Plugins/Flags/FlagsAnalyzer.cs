using System;
using System.Collections.Generic;
using Kari.GeneratorCore;
using Kari.GeneratorCore.Workflow;
using Kari.Utils;
using Microsoft.CodeAnalysis;

namespace Kari.Plugins.Flags
{
    public class FlagsInfo
    {
        public FlagsInfo(INamedTypeSymbol symbol)
        {
            Name = symbol.Name;
            FullName = symbol.GetFullyQualifiedName();
        }

        public readonly string Name;
        public readonly string FullName;
    }

    public partial class FlagsAnalyzer : IAnalyzer, ITransformText
    {
        public readonly List<FlagsInfo> _infos = new List<FlagsInfo>();

        public void Collect(ProjectEnvironment environment)
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

        /// <summary>
        /// Returns all possible combinations of the set bits of the given $(Name).
        /// For example, for the input 0111 it would give 0001, 0010, 0011, 0100, 0101 and 0111.
        /// </summary>
        public static IEnumerable<$(FullName)> GetBitCombinations(this $(FullName) flags)
        {
            int bits = (int) flags;
            int current = (~bits + 1) & bits;

            while (current != 0)
            {
                yield return ($(FullName)) current;
                current = (~bits + (current << 1)) & bits;
            }
        }
        
        /// <summary>
        /// Returns all individual set bits of the given $(Name) on their positions.
        /// For example, for the input 0111 it would give 0001, 0010 and 0100.
        /// </summary>
        public static IEnumerable<$(FullName)> GetSetBits(this $(FullName) flags)
        {
            int bits = (int) flags;
            int current = 0;
            
            while (true)
            {
                current = (current - bits) & bits;
                if (current == 0) yield break;
                yield return ($(FullName)) current;
            }
        }
    }
    ";

        public void TransformSingle(FlagsInfo info, ref CodeBuilder builder)
        {
            builder.FormattedAppend(template, 
                "Name", info.Name, 
                "FullName", info.FullName);
        }

        public string TransformText(ProjectEnvironmentData project)
        {
            if (_infos.Count == 0)
                return null;

            var builder = new CodeBuilder("    ");
            builder.AppendLine("namespace " + project.GeneratedNamespaceName);
            builder.StartBlock();
            builder.AppendLine("System.Collections.Generic;");
            foreach (var info in _infos) 
                TransformSingle(info, ref builder);
            builder.EndBlock();

            return builder.ToString();
        }
    }
}
