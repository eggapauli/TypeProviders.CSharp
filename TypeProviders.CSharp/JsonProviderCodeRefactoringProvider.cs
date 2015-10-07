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
using System.Collections.Generic;

namespace TypeProviders.CSharp
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(JsonProviderCodeRefactoringProvider)), Shared]
    public class JsonProviderCodeRefactoringProvider : CodeRefactoringProvider
    {
        static readonly string AttributeFullName = typeof(Providers.JsonProviderAttribute).FullName;

        public bool AddCreationMethods { get; set; } = true;

        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context
                .Document
                .GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false);

            var node = root.FindNode(context.Span);

            var typeDecl = node as ClassDeclarationSyntax;
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

        async Task<Document> UpdateTypeProviderAsync(Document document, ClassDeclarationSyntax typeDecl, string sampleData, CancellationToken ct)
        {
            var data = await GetData(sampleData);

            var members = GetTypeProviderMembers(typeDecl, data);
            var newTypeDecl = typeDecl
                .WithMembers(SyntaxFactory.List(members))
                .WithAdditionalAnnotations(Formatter.Annotation);
            var syntaxRoot = await document.GetSyntaxRootAsync();
            return document.WithSyntaxRoot(syntaxRoot.ReplaceNode(typeDecl, newTypeDecl));
        }

        IEnumerable<MemberDeclarationSyntax> GetTypeProviderMembers(TypeDeclarationSyntax typeDecl, JToken data)
        {
            var jObj = data as JObject;
            if (jObj != null)
            {
                foreach (var property in jObj.Properties())
                {
                    if (property.Value.Type == JTokenType.Object)
                    {
                        var subTypeName = GetIdentifierName(property.Name);
                        var subTypeDecl = SyntaxFactory.ClassDeclaration(subTypeName)
                            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken));

                        var type = SyntaxFactory.ParseTypeName(subTypeName);
                        var propertyName = GetIdentifierName(property.Name);
                        var propertyDecl = GetPropertyDeclaration(type, propertyName);
                        yield return propertyDecl;

                        var subTypeMembers = GetTypeProviderMembers(subTypeDecl, property.Value);
                        yield return subTypeDecl.WithMembers(SyntaxFactory.List(subTypeMembers));
                    }
                    else
                    {
                        var type = GetTypeFromToken(property.Value);
                        var propertyName = GetIdentifierName(property.Name);
                        var propertyDecl = GetPropertyDeclaration(type, propertyName);
                        yield return propertyDecl;
                    }
                }
            }
        }

        PropertyDeclarationSyntax GetPropertyDeclaration(TypeSyntax type, string propertyName)
        {
            return
                SyntaxFactory.PropertyDeclaration(type, propertyName)
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
                    );
        }

        string GetIdentifierName(string jsonPropertyName)
        {
            return char.ToUpper(jsonPropertyName[0]) + jsonPropertyName.Substring(1);
        }

        TypeSyntax GetTypeFromToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Boolean:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword));
                case JTokenType.Date:
                    return SyntaxFactory.ParseTypeName("System.DateTime");
                case JTokenType.Float:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DoubleKeyword));
                case JTokenType.Integer:
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword));
                case JTokenType.String:
                    var value = (string)token;
                    Guid guid;
                    if (Guid.TryParse(value, out guid))
                        return SyntaxFactory.ParseTypeName("System.Guid");
                    TimeSpan timeSpan;
                    if (TimeSpan.TryParse(value, out timeSpan))
                        return SyntaxFactory.ParseTypeName("System.TimeSpan");
                    Uri uri;
                    if (Uri.TryCreate(value, UriKind.Absolute, out uri))
                        return SyntaxFactory.ParseTypeName("System.Uri");

                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword));
                case JTokenType.Object:

                default:
                    throw new NotImplementedException("Unknown token type: " + token.Type);
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