using System;
using System.Threading;
using System.Threading.Tasks;
using CodeGeneration.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Kari.Generators
{
    public class TestGenerator : ICodeGenerator
    {
        public TestGenerator(AttributeData attributeData) 
        {
        }

        public Task<SyntaxList<MemberDeclarationSyntax>> GenerateAsync(
            TransformationContext context, IProgress<Diagnostic> progress, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                "public class World{}".ToMemberSyntaxList()
            );
        }
    }
}
