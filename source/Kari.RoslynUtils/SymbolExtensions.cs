using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static System.Diagnostics.Debug;

namespace Kari.GeneratorCore.Workflow
{
    public static class SymbolExtensions
    {
        internal static string GetFullyQualifiedName(this ISymbol symbol)
        {
            if (symbol.Kind == SymbolKind.ErrorType
                || symbol.ContainingNamespace is null
                && symbol.ContainingType is null)
            {
                return symbol.Name;
            }
            return $"{GetFullQualification(symbol)}.{symbol.Name}";
        }

        public static string GetSpecialName(SpecialType specialType)
        {
            return specialType switch 
            {
                SpecialType.System_SByte    => "sbyte",
                SpecialType.System_Int16    => "short",
                SpecialType.System_Int32    => "int",
                SpecialType.System_Int64    => "long",
                SpecialType.System_Byte     => "byte",
                SpecialType.System_UInt16   => "ushort",
                SpecialType.System_UInt32   => "uint",
                SpecialType.System_UInt64   => "ulong",
                SpecialType.System_Object   => "object",
                SpecialType.System_Boolean  => "bool",
                SpecialType.System_String   => "string",
                SpecialType.System_Single   => "float",
                SpecialType.System_Double   => "double",
                SpecialType.System_Char     => "char",
                _ => null,
            };
        }

        public static string GetFullyQualifiedName(this ITypeSymbol symbol)
        {
            return GetSpecialName(symbol.SpecialType) ?? GetFullyQualifiedName((ISymbol) symbol);
        }

        public static string GetFullQualification(this ISymbol symbol)
        {
            Stack<string> names = new Stack<string>();

            while (symbol.ContainingType != null && symbol.ContainingType.Name != "")
            {
                names.Push(symbol.ContainingType.Name);
                symbol = symbol.ContainingType;
            }

            foreach (var n in symbol.ContainingNamespace.GetNamespaceNames())
            {
                names.Push(n);
            }

            return String.Join(".", names);
        }

        public static IEnumerable<string> GetNamespaceNames(this INamespaceSymbol symbol)
        {
            while (symbol != null && symbol.Name != "")
            {
                yield return symbol.Name;
                symbol = symbol.ContainingNamespace;
            }
        }

        public static string GetTypeQualification(this ISymbol symbol)
        {
            Stack<string> names = new Stack<string>();

            while (symbol.ContainingType != null && symbol.ContainingType.Name != "")
            {
                names.Push(symbol.ContainingType.Name);
                symbol = symbol.ContainingType;
            }

            return String.Join(".", names);
        }

        public static string GetFullName(this INamespaceSymbol symbol)
        {
            Stack<string> names = new Stack<string>();

            foreach (var n in symbol.GetNamespaceNames())
            {
                names.Push(n);
            }

            return String.Join(".", names);
        }

        public static string ToFullyQualifiedText(this ITypeSymbol symbol)
        {
            StringBuilder t = new();
            ToFullyQualifiedText(symbol, t);
            return t.ToString();
        }

        public static void ToFullyQualifiedText(this ITypeSymbol symbol, StringBuilder sb_type)
        {
            string specialName = GetSpecialName(symbol.SpecialType);
            if (specialName is not null)
            {
                sb_type.Append(specialName);
                return;
            }
            
            if (symbol.Kind == SymbolKind.ErrorType)
            {
                sb_type.Append(symbol.Name);
                return;
            }
            var qualification = symbol.GetFullQualification();
            if (qualification.Length > 0)
            {
                sb_type.Append(qualification);
                sb_type.Append(".");
            }
            TypeToTextUncertainBit(symbol, sb_type);
        }

        public static string GetSimpleName(this ITypeSymbol symbol)
        {
            return GetSpecialName(symbol.SpecialType) ?? symbol.Name;
        }

        private static void TypeToTextUncertainBit(ITypeSymbol symbol, StringBuilder sb_type)
        {
            if (symbol is INamedTypeSymbol named_symbol)
            {
                sb_type.Append(symbol.Name);

                if (named_symbol.IsGenericType)
                {
                    sb_type.Append("<");

                    foreach (var t in named_symbol.TypeArguments)
                    {
                        ToFullyQualifiedText((INamedTypeSymbol) t, sb_type);
                        sb_type.Append(", ");
                    }

                    sb_type.Remove(sb_type.Length - 2, 2);
                    sb_type.Append(">");
                }
            }
            else if (symbol is IArrayTypeSymbol array_symbol)
            {
                var type = array_symbol.ElementType;
                TypeToTextUncertainBit(type, sb_type);
                sb_type.Append("[]");
            }
        }

        public static IEnumerable<string> ParamNames(this IEnumerable<IFieldSymbol> fields)
        {
            return fields.Select(p => p.Name);
        }

        public static IEnumerable<string> ParamNames(this IEnumerable<IParameterSymbol> parameters)
        {
            return parameters.Select(p => p.Name);
        }

        public static IEnumerable<string> ParamTypeNames(this IEnumerable<IFieldSymbol> fields)
        {
            return fields.Select(p => p.Type.Name);
        }

        public static IEnumerable<string> ParamTypeNames(this IEnumerable<IParameterSymbol> parameters)
        {
            return parameters.Select(p => p.Name);
        }

        public static string JoinedParamNames(this IEnumerable<IFieldSymbol> fields)
        {
            return String.Join(", ", fields.Select(p => p.Name));
        }

        public static string JoinedParamNames(this IEnumerable<IParameterSymbol> parameters)
        {
            return String.Join(", ", parameters.Select(p => p.Name));
        }

        public static string JoinedParamTypeNames(this IEnumerable<IFieldSymbol> fields)
        {
            return String.Join(", ", ParamTypeNames(fields));
        }

        public static string JoinedParamTypeNames(this IEnumerable<IParameterSymbol> parameters)
        {
            return String.Join(", ", ParamTypeNames(parameters));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfFirst<T>(this IEnumerable<T> e, Predicate<T> predicate)
        {
            int i = 0;
            foreach (var el in e)
            {
                if (predicate(el)) 
                    return i;
                i++;
            }
            return -1;
        }
        
        public static IEnumerable<IMethodSymbol> GetMethods(this ITypeSymbol symbol)
        {
            return symbol.GetMembers().OfType<IMethodSymbol>();
        }

        public static bool ParameterTypesEqual(this IMethodSymbol method, IEnumerable<IFieldSymbol> fields)
        {
            return method.Parameters.Select(m1 => m1.Type).SequenceEqual(
                fields.Select(field => field.Type), SymbolEqualityComparer.Default);
        }

        public static bool TypeSequenceEqual(this IEnumerable<IParameterSymbol> parameters, IEnumerable<IFieldSymbol> fields)
        {
            return parameters.Select(m1 => m1.Type).SequenceEqual(
                fields.Select(field => field.Type), SymbolEqualityComparer.Default);
        }

        public static bool HasInterface(this ITypeSymbol symbol, ISymbol interfaceType)
        {
            var interfaces = symbol.AllInterfaces;
            for (int i = 0; i < interfaces.Length; i++)
            {
                if (SymbolEqualityComparer.Default.Equals(interfaces[i], interfaceType))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsContainedInNamespace(this ISymbol symbol, INamespaceSymbol namespaceSymbol)
        {
            while (symbol.ContainingType != null) symbol = symbol.ContainingType;
            while (symbol.ContainingNamespace != null)
            {
                symbol = symbol.ContainingNamespace;
                if (namespaceSymbol == symbol)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the base types of the current type, including the current type.
        /// The types are returned in order from most derived to least derived.
        /// </summary>
        public static IEnumerable<INamedTypeSymbol> GetReversedTypeHierarchy(this INamedTypeSymbol symbol)
        {
            while (symbol != null) 
            {
                yield return symbol;
                symbol = symbol.BaseType;
            }
        }

        /// <summary>
        /// Returns the base types of the current type, including the current type.
        /// The types are returned in order from least derived to most derived.
        /// </summary>
        public static IEnumerable<INamedTypeSymbol> GetTypeHierarchy(this INamedTypeSymbol symbol)
        {
            return GetReversedTypeHierarchy(symbol).Reverse();
        }

        public static IEnumerable<IFieldSymbol> GetFields(this INamedTypeSymbol symbol)
        {
            return symbol.GetMembers().OfType<IFieldSymbol>();
        }

        public static IEnumerable<IFieldSymbol> GetInstanceFields(this INamedTypeSymbol symbol)
        {
            return symbol.GetFields().Where(f => !f.IsStatic && !f.IsConst);
        }

        public static IEnumerable<IFieldSymbol> GetStaticFields(this INamedTypeSymbol symbol)
        {
            return symbol.GetFields().Where(f => f.IsStatic);
        }

        public static string CommaJoin<T>(this IEnumerable<T> things, System.Func<T, string> func)
        {
            return System.String.Join(", ", things.Select(func));
        }

        public static string AsKeyword(this RefKind kind) => kind switch 
        {
            RefKind.In  => "in",
            RefKind.Out => "out",
            RefKind.Ref => "ref",
            _           => ""
        };

        public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T> e) where T : class
        {
            return e.Where(el => el is not null);
        }
        
        public static IEnumerable<ITypeSymbol> GetLeafTypeArguments(this ITypeSymbol symbol)
        {
            if (symbol is INamedTypeSymbol named_symbol && named_symbol.IsGenericType)
            {
                foreach (var type_argument in named_symbol.TypeArguments) 
                foreach (var nested_symbol in GetLeafTypeArguments(type_argument))
                    yield return nested_symbol;
            }
            else
            {
                yield return symbol;
            }
        }

        public static INamespaceSymbol GetNamespace(this Compilation compilation, string nspace)
        {
            var paths = nspace.Split('.');

            INamespaceSymbol result = compilation.GlobalNamespace;

            for (int i = 0; i < paths.Length; i++)
            {
                result = result.GetNamespaceMembers().Where(ns => ns.Name == paths[i]).Single();
            }

            return result;
        }

        public static INamespaceSymbol TryGetNamespace(this Compilation compilation, string nspace)
        {
            var paths = nspace.Split('.');

            INamespaceSymbol result = compilation.GlobalNamespace;

            for (int i = 0; i < paths.Length; i++)
            {
                result = result.GetNamespaceMembers().Where(ns => ns.Name == paths[i]).FirstOrDefault();
                if (result is null) 
                    return null;
            }

            return result;
        }

        /// <summary>
        /// Get all top-level type symbols of the given namespace and all namespaces within it.
        /// </summary>
        public static IEnumerable<INamedTypeSymbol> GetNotNestedTypes(this INamespaceSymbol nspace)
        {
            foreach (var type in nspace.GetTypeMembers())
                yield return type;

            foreach (var nestedNamespace in nspace.GetNamespaceMembers())
            foreach (var type in GetNotNestedTypes(nestedNamespace))
                yield return type;
        }

        /// <summary>
        /// Returns "null" for reference types and "default" for value types.
        /// For some special types, like bool and ints, "false" and "0" are returned.
        /// </summary>
        public static string GetDefaultValueText(this IParameterSymbol parameter)
        {
            // var syntax = (ParameterSyntax) parameter.DeclaringSyntaxReferences[0].GetSyntax();

            if (parameter.Type.SpecialType == SpecialType.None)
            {
                if (parameter.Type.IsReferenceType) 
                    return "null";
                else 
                    return "default";
            }

            switch (parameter.Type.SpecialType)
            {
                case SpecialType.System_Boolean:
                    return "false";
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return "0";
                case SpecialType.System_String:
                    return "null";
            }

            return "default";
        }

        public static string Join(this string mainPart, string part, string separator = ".")
        {
            if (mainPart == "") return part;
            return mainPart + separator + part;
        }

        public static string GetLocationInfo(this ISymbol symbol)
        {
            var locations = symbol.Locations;
            if (locations.Length == 0) return "Unknown location";
            var span = locations[0].GetLineSpan();
            return $"{locations[0].SourceTree.FilePath}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}";
        }

        public static XmlDocument GetDocumentationXml(this ISymbol symbol)
        {
            var comment = symbol.GetDocumentationCommentXml();
            if (string.IsNullOrEmpty(comment)) return null;
            var xml = new XmlDocument();
            xml.LoadXml(comment);
            return xml;
        }

        public static string EscapeVerbatim(this string text)
        {
            return text.Replace("\"", "\"\"");
        }

        public static string AsVerbatimSyntax(this string text)
        {
            return $"@\"{text.EscapeVerbatim()}\"";
        }

        public static bool HasEqualityOperatorAbleToCompareAgainstOwnType(this ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Enum:
                case SpecialType.System_Boolean:
                case SpecialType.System_Char:
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Decimal:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_String:
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                case SpecialType.System_DateTime:
                    return true;
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            var arr = type.GetMembers("op_Equality");
            for (int i = 0; i < arr.Length; i++)
            {
                var opMethod = arr[i] as IMethodSymbol;

                // AFAIK it has to be a method.
                Assert(opMethod is not null);

                if (opMethod.Parameters.Length == 2 &&
                    type.Equals(opMethod.Parameters[0].Type) &&
                    type.Equals(opMethod.Parameters[1].Type))
                {
                    return true;
                }
            }

            return false;
        }
    }
}