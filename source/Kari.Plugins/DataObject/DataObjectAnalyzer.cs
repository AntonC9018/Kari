using System.Collections.Generic;
using System.Linq;
using Kari.GeneratorCore;
using Kari.GeneratorCore.Workflow;
using Kari.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kari.Plugins.DataObject
{
    public class DataObjectAnalyzer : ICollectSymbols, IGenerateCode
    {
        public readonly List<DataObjectInfo> _infos = new List<DataObjectInfo>();

        public void CollectSymbols(ProjectEnvironment environment)
        {
            foreach (var type in environment.TypesWithAttributes)
            {
                if (type.HasDataObjectAttribute(environment.Compilation))
                {
                    var syntax = type.DeclaringSyntaxReferences[0].GetSyntax() as TypeDeclarationSyntax;
                    if (!syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                    {
                        environment.Logger.LogWarning($"The type '{type.Name}' defined at {type.GetLocationInfo()} marked as 'DataObject' must be partial.");
                    }
                    if (type.IsStatic)
                    {
                        environment.Logger.LogError($"'DataObjects' cannot be static. {type.GetLocationInfo()}");
                    }

                    string accessModifier;
                    if (syntax.Modifiers.Count == 0)
                    {
                        environment.Logger.LogWarning($"'DataObjects' must have an access modifier. {type.GetLocationInfo()}");
                        accessModifier = "public";
                    }
                    else
                    {
                        accessModifier = syntax.Modifiers[0].Text;
                    }

                    _infos.Add(new DataObjectInfo(type, accessModifier));
                }
            }
        }

        public void GenerateCode(ProjectEnvironmentData p, ref CodeBuilder cb)
        {
            foreach (var info in _infos)
            {
                cb.AppendLine("namespace ", info.Symbol.GetFullQualification());
                cb.StartBlock();
                AppendCodeForSingleInfo(ref cb, info);
                cb.EndBlock();
            }
        }

        public void AppendCodeForSingleInfo(ref CodeBuilder cb, in DataObjectInfo info)
        {
            cb.AppendLine($"{info.AccessModifier} partial {info.TypeKeyword} {info.NameTypeParameters}");
            cb.StartBlock();

            {
                cb.AppendLine($"public static bool operator==({info.NameTypeParameters} a, {info.NameTypeParameters} b)");
                cb.StartBlock();

                if (info.Symbol.IsReferenceType)
                {
                    cb.AppendLine("if (a is null && b is null)");
                    cb.IncreaseIndent();
                    cb.AppendLine("return true;");
                    cb.DecreaseIndent();

                    cb.AppendLine("if (a is null || b is null)");
                    cb.IncreaseIndent();
                    cb.AppendLine("return false;");
                    cb.DecreaseIndent();
                }

                cb.Indent();
                cb.Append("return ");

                var listBuilder = CodeListBuilder.Create("&& ");
                foreach (var field in info.Fields)
                {
                    // TODO: Check if this works for hierarchies.
                    // TODO: The lib code for symbols that are not part of the user code is not loaded, so this check will be wrong. 
                    // if (field.Type.HasEqualityOperatorAbleToCompareAgainstOwnType())
                        
                        // You know what? we should just check the references in this case.
                        listBuilder.AppendOnNewLine(ref cb, $"a.{field.Name} == b.{field.Name}");

                    // else
                    //     listBuilder.AppendOnNewLine(ref cb, $"a.{field.Name}.Equals(b.{field.Name})");
                }

                if (info.Fields.Length == 0)
                    cb.Append("true");

                cb.Append(";");
                cb.NewLine();
                cb.EndBlock();

                cb.AppendLine($"public static bool operator!=({info.NameTypeParameters} a, {info.NameTypeParameters} b)");
                cb.StartBlock();
                cb.AppendLine("return !(a == b);");
                cb.EndBlock();
            }

            if (!info.Symbol.IsReadOnly)
            {
                cb.AppendLine($"public void Sync({info.NameTypeParameters} other)");
                cb.StartBlock();

                foreach (var field in info.Fields)
                {
                    cb.AppendLine($"this.{field.Name} = other.{field.Name};");
                }

                cb.EndBlock();
            }

            {
                cb.AppendLine("public override int GetHashCode()");
                cb.StartBlock();
                cb.AppendLine("unchecked");
                cb.StartBlock();
                cb.AppendLine("int hash = 17;");

                foreach (var field in info.Fields)
                {
                    cb.AppendLine($"hash = hash * 23 + {field.Name}.GetHashCode();");
                }
                cb.AppendLine("return hash;");
                cb.EndBlock();
                cb.EndBlock();
            }

            {
                cb.AppendLine($"public override bool Equals(object other)");
                cb.StartBlock();
                cb.AppendLine($"return other is {info.NameTypeParameters} a && this == a;");
                cb.EndBlock();
            }
            
            if (info.Symbol.IsReferenceType) 
            {
                cb.AppendLine($"public {info.NameTypeParameters} Copy => ({info.NameTypeParameters}) this.MemberwiseClone();");
            }
            else
            {
                cb.AppendLine($"public {info.NameTypeParameters} Copy => this;");
            }

            cb.EndBlock();
        }
    }

    public readonly struct DataObjectInfo
    {
        public readonly IFieldSymbol[] Fields;
        public readonly INamedTypeSymbol Symbol;
        public readonly string AccessModifier;
        public readonly string NameTypeParameters;
        public string TypeKeyword => Symbol.IsValueType ? "struct" : "class";
        public string Name => Symbol.Name;

        public DataObjectInfo(INamedTypeSymbol symbol, string accessModifier)
        {
            Symbol = symbol;
            AccessModifier = accessModifier;
            Fields = Symbol.GetMembers().OfType<IFieldSymbol>().Where(s => !s.IsStatic).ToArray();
            
            var syntax = Symbol.DeclaringSyntaxReferences[0].GetSyntax() as TypeDeclarationSyntax;
            NameTypeParameters = symbol.Name;
            if (!(syntax.TypeParameterList is null))
                NameTypeParameters += syntax.TypeParameterList.WithoutTrivia().ToFullString();
        }
    }
}
