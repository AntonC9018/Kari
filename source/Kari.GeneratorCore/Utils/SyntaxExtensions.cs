using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Kari.GeneratorCore.Workflow
{
    public static class SyntaxExtensions
    {
        public static NameSyntax GetFullyQualifiedNameSyntax(this ITypeSymbol type)
        {
            List<INamespaceOrTypeSymbol> above = new();

            INamespaceOrTypeSymbol t = type;
            while (t.ContainingType is ITypeSymbol t1)
            {
                t = t1;
                above.Add(t);
            }
            while (t.ContainingNamespace is INamespaceSymbol t1
                && !t1.IsGlobalNamespace)
            {
                t = t1;
                above.Add(t);
            }

            NameSyntax result = IdentifierName(above[^1].Name);
            for (int i = above.Count - 2; i >= 0; i--)
                result = QualifiedName(result, IdentifierName(above[i].Name));
            
            result = QualifiedName(result, IdentifierName(type.Name));
            return result;
        }

        public static (ITypeSymbol Symbol, TypeSyntax Syntax) GetFullyQualifiedTypeNameSyntax(this TypeSyntax name, SemanticModel semanticModel)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(name);

            if (symbolInfo.Symbol is not ITypeSymbol typeSymbol)
            {
                if (symbolInfo.CandidateSymbols.Length == 0)
                    return (null, name);
                else
                    typeSymbol = symbolInfo.CandidateSymbols[0] as ITypeSymbol;
                
                if (typeSymbol is null)
                    return (null, name);
            }

            return (typeSymbol, GetFullyQualifiedNameSyntax(typeSymbol));
        }

        

        // <left> = <right>
        public static AssignmentExpressionSyntax Assignment(this ExpressionSyntax left, ExpressionSyntax right)
        {
            return SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                left,
                right);
        }

        // <left> = <right>
        public static AssignmentExpressionSyntax Assignment(this ExpressionSyntax left, string right)
        {
            return Assignment(left, IdentifierName(right));
        }

        public static SyntaxTokenList GetGetModifiers(this PropertyDeclarationSyntax property, bool isClass)
        {
            if (property.ExpressionBody is not null)
            {
                var mods = property.Modifiers;
                return GetGetModifiersFromNonAccessor(mods, isClass);
            }

            var accessors = property.AccessorList.Accessors;
            var getter = accessors.FirstOrDefault(a => a.Kind() == SyntaxKind.GetAccessorDeclaration);
            if (getter is not null)
                return getter.Modifiers;

            return default;
        }

        public static SyntaxTokenList GetGetModifiersFromNonAccessor(this SyntaxTokenList nonAccessorModifiers, bool isClass)
        {
            if (isClass)
                return default;

            // Transfer the readonly keyword only to the getter.
            foreach (var m in nonAccessorModifiers)
            {
                if (m.Kind() == SyntaxKind.ReadOnlyKeyword)
                    return new(m);
            }
            return default;
        }

        public static SyntaxTokenList GetSetModifiers(this PropertyDeclarationSyntax property)
        {
            var accessors = property.AccessorList.Accessors;
            var setter = accessors.FirstOrDefault(a => a.Kind() == SyntaxKind.SetAccessorDeclaration);
            if (setter is not null)
                return setter.Modifiers;

            return default;
        }

        public static SyntaxTokenList GetNonAccessorModifiers(this PropertyDeclarationSyntax property)
        {
            return GetNonAccessor(property.Modifiers);
        }

        public static SyntaxTokenList GetNonAccessor(this SyntaxTokenList modifiers)
        {
            return new(modifiers.Where(m => m.Kind() != SyntaxKind.ReadOnlyKeyword));
        }
    }

    public static class SyntaxHelper
    {
        // get => <whatever>;
        public static AccessorDeclarationSyntax GetAccessorArrow(ExpressionSyntax whatever, SyntaxTokenList modifiers = default)
        {
            return AccessorDeclaration(
                SyntaxKind.GetAccessorDeclaration,
                expressionBody: ArrowExpressionClause(whatever),
                attributeLists: default,
                modifiers: modifiers,
                body: null,
                keyword: Token(SyntaxKind.GetKeyword),
                semicolonToken: Token(SyntaxKind.SemicolonToken));
        }

        // set => <whatever>;
        public static AccessorDeclarationSyntax SetAccessorArrow(ExpressionSyntax whatever, SyntaxTokenList modifiers = default)
        {
            return AccessorDeclaration(
                SyntaxKind.SetAccessorDeclaration,
                expressionBody: ArrowExpressionClause(whatever),
                attributeLists: default,
                modifiers: modifiers,
                body: null,
                keyword: Token(SyntaxKind.SetKeyword),
                semicolonToken: Token(SyntaxKind.SemicolonToken));
        }

        public static NamespaceDeclarationSyntax NamespaceBlock(string name)
        {
            return NamespaceBlock(IdentifierName(name));
        }

        public static NamespaceDeclarationSyntax NamespaceBlockForType(ITypeSymbol type)
        {
            return NamespaceBlock(IdentifierName(type.ContainingNamespace.GetFullyQualifiedName().ToString()));
        }

        public static NamespaceDeclarationSyntax WrapPartialType(ITypeSymbol type, TypeDeclarationSyntax partialTypeSyntax)
        {
            var nspace = NamespaceBlockForType(type);

            var outerTypeSyntax = partialTypeSyntax;

            ITypeSymbol outer = type.ContainingType;
            while (outer is not null)
            {
                var t = (TypeDeclarationSyntax) outer.DeclaringSyntaxReferences[0].GetSyntax();
                outerTypeSyntax = t.WithMembers(new(outerTypeSyntax)).WithAttributeLists(default);

                outer = outer.ContainingType;
            }

            return nspace.WithMembers(new(outerTypeSyntax));
        }

        public static NamespaceDeclarationSyntax NamespaceBlock(NameSyntax name)
        {
            return NamespaceDeclaration(
                namespaceKeyword: SyntaxFactory.Token(SyntaxKind.NamespaceKeyword),
                openBraceToken: SyntaxFactory.Token(SyntaxKind.OpenBraceToken),
                externs: default,
                members: default,
                usings: default,
                closeBraceToken: SyntaxFactory.Token(SyntaxKind.CloseBraceToken),
                semicolonToken: default,
                name: name);
        }
    }
}