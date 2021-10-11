using System.Collections.Generic;
using System.Linq;
using Kari.GeneratorCore;
using Kari.GeneratorCore.Workflow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kari.Plugins.DataObject
{
    public class DataObjectAnalyzer : IAnalyzer
    {
        public readonly List<DataObjectInfo> _infos = new List<DataObjectInfo>();

        public void Collect(ProjectEnvironment environment)
        {
            foreach (var type in environment.TypesWithAttributes)
            {
                if (type.HasAttribute(DataObjectSymbols.DataObjectAttribute.symbol))
                {
                    var syntax = type.DeclaringSyntaxReferences[0].GetSyntax() as ClassDeclarationSyntax;
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

        public string TransformSingle(DataObjectInfo info, string initialIndentation = "    ")
        {
            var cb = new CodeBuilder("    ", initialIndentation);
            cb.AppendLine($"{info.AccessModifier} partial {info.TypeKeyword} {info.Name}");
            cb.StartBlock();

            {
                cb.AppendLine($"public static bool operator==({info.Name} a, {info.Name} b)");
                cb.StartBlock();
                cb.Indent();
                cb.Append("return ");

                var listBuilder = new ListBuilder($"\r\n{cb.CurrentIndentation + cb.Indentation}&& ");
                foreach (var field in info.Fields)
                {
                    listBuilder.Append($"a.{field.Name} == b.{field.Name}");
                }

                cb.Append(listBuilder.ToString());

                if (info.Fields.Length == 0)
                {
                    cb.Append("true");
                }
                cb.Append(";");
                cb.AppendLine();
                cb.EndBlock();

                cb.AppendLine($"public static bool operator!=({info.Name} a, {info.Name} b)");
                cb.StartBlock();
                cb.AppendLine("return !(a == b);");
                cb.EndBlock();
            }

            {
                cb.AppendLine($"public void Sync({info.Name} other)");
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
                cb.AppendLine($"return other is {info.Name} a && this == a;");
                cb.EndBlock();
            }

            if (info.Symbol.IsReferenceType) 
            {
                cb.AppendLine($"public {info.Name} Copy => ({info.Name}) this.MemberwiseClone();");
            }

            cb.EndBlock();
            return cb.ToString();
        }
    }

    public readonly struct DataObjectInfo
    {
        public readonly IFieldSymbol[] Fields;
        public readonly INamedTypeSymbol Symbol;
        public readonly string AccessModifier;
        public string TypeKeyword => Symbol.IsValueType ? "struct" : "class";
        public string Name => Symbol.Name;

        public DataObjectInfo(INamedTypeSymbol symbol, string accessModifier)
        {
            Symbol = symbol;
            AccessModifier = accessModifier;
            Fields = Symbol.GetMembers().OfType<IFieldSymbol>().ToArray();
        }
    }
}