using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Net.Http;
using Microsoft.CodeAnalysis.Editing;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Microsoft.CodeAnalysis.Formatting;

namespace TypeProviders.CSharp
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(JsonProviderCodeRefactoringProvider)), Shared]
    public class JsonProviderCodeRefactoringProvider : CodeRefactoringProvider
    {
        static readonly string AttributeFullName = typeof(Providers.JsonProviderAttribute).FullName;

        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context
                .Document
                .GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false);

            var node = root.FindNode(context.Span);

            var typeDecl = node as TypeDeclarationSyntax;
            if (typeDecl == null) return;

            var compilation = await context.Document.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false);
            var attributeSymbol = compilation.GetTypeByMetadataName(AttributeFullName);
            if (attributeSymbol == null) return;

            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
            
            var attribute = typeSymbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass.Equals(attributeSymbol));
            if (attribute == null) return;

            var sampleSourceArgument = attribute.ConstructorArguments.FirstOrDefault();
            if (sampleSourceArgument.IsNull) return;

            var sampleData = sampleSourceArgument.Value as string;
            if (sampleData == null) return;

            var action = CodeAction.Create("Synchronize type provider with sample data", c => UpdateTypeProviderAsync(context.Document, typeDecl, sampleData, c));

            context.RegisterRefactoring(action);
        }

        async Task<Document> UpdateTypeProviderAsync(Document document, TypeDeclarationSyntax typeDecl, string sampleData, CancellationToken ct)
        {
        }
    }
}