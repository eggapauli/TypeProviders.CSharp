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
            var data = await GetData(sampleData);

            var documentEditor = await DocumentEditor.CreateAsync(document);
            var jObj = data as JObject;
            if (jObj != null)
            {
                foreach (var property in jObj.Properties())
                {
                    var propertyDeclaration = SyntaxFactory.PropertyDeclaration
                        ( GetTokenType(property.Value)
                        , property.Name
                        )
                        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                        .WithAccessorList(
                            SyntaxFactory.AccessorList(
                                SyntaxFactory.List(
                                    new[]
                                    {
                                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
                                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                                    }
                                )
                            )
                            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
                        ).WithAdditionalAnnotations(Formatter.Annotation);
                    documentEditor.AddMember(typeDecl, propertyDeclaration);
                }
            }
            return documentEditor.GetChangedDocument();
        }

        TypeSyntax GetTokenType(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.String:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword));
                default:
                    throw new NotImplementedException();
            }
        }

        static async Task<JToken> GetData(string sampleData)
        {
            Uri sampleDataUri;
            if (Uri.TryCreate(sampleData, UriKind.Absolute, out sampleDataUri))
            {
                using (var client = new HttpClient())
                {
                    var data = await client.GetStringAsync(sampleDataUri);
                }
            }
            return JToken.Parse(sampleData);
        }
    }
}