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

        public bool AddDataStructure { get; set; } = true;
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
            try {
                var data = await GetData(sampleData);

                var members = Enumerable.Empty<MemberDeclarationSyntax>();

                if (AddDataStructure)
                {
                    members = members.Concat(GetPropertiesFromData(typeDecl, data));
                }
                if (AddCreationMethods)
                {
                    members = members.Concat(GetCreationMethods(typeDecl));
                }

                var newTypeDecl = typeDecl
                    .WithMembers(SyntaxFactory.List(members))
                    .WithAdditionalAnnotations(Formatter.Annotation);
                var syntaxRoot = await document.GetSyntaxRootAsync();
                return document.WithSyntaxRoot(syntaxRoot.ReplaceNode(typeDecl, newTypeDecl));
            }
            catch(Exception e)
            {
                return document;
            }
        }

        static IEnumerable<MemberDeclarationSyntax> GetPropertiesFromData(TypeDeclarationSyntax typeDecl, JToken data)
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

                        var subTypeMembers = GetPropertiesFromData(subTypeDecl, property.Value);
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

        static PropertyDeclarationSyntax GetPropertyDeclaration(TypeSyntax type, string propertyName)
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

        static string GetIdentifierName(string jsonPropertyName)
        {
            return char.ToUpper(jsonPropertyName[0]) + jsonPropertyName.Substring(1);
        }

        static TypeSyntax GetTypeFromToken(JToken token)
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

        static IEnumerable<MemberDeclarationSyntax> GetCreationMethods(TypeDeclarationSyntax typeDecl)
        {

            yield return SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName($"System.Threading.Tasks.Task<{typeDecl.Identifier.Text}>"), "LoadAsync")
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.AsyncKeyword)))
                .WithParameterList(SyntaxFactory.ParameterList
                    (SyntaxFactory.Token(SyntaxKind.OpenParenToken)
                    , SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Parameter(SyntaxFactory.Identifier("uri")).WithType(SyntaxFactory.ParseTypeName("System.Uri")))
                    , SyntaxFactory.Token(SyntaxKind.CloseParenToken)
                    ))
                .WithBody(SyntaxFactory.Block(SyntaxFactory.UsingStatement
                    (SyntaxFactory.Token(SyntaxKind.UsingKeyword)
                    , SyntaxFactory.Token(SyntaxKind.OpenParenToken)
                    , SyntaxFactory.VariableDeclaration
                        ( SyntaxFactory.IdentifierName("var")
                        , SyntaxFactory.SingletonSeparatedList
                            ( SyntaxFactory.VariableDeclarator("client")
                                .WithInitializer(SyntaxFactory.EqualsValueClause
                                    ( SyntaxFactory.ObjectCreationExpression
                                        (SyntaxFactory.Token(SyntaxKind.NewKeyword)
                                        , SyntaxFactory.ParseTypeName("System.Net.Http.HttpClient")
                                        , SyntaxFactory.ArgumentList()
                                            .WithOpenParenToken(SyntaxFactory.Token(SyntaxKind.OpenParenToken))
                                            .WithCloseParenToken(SyntaxFactory.Token(SyntaxKind.CloseParenToken))
                                        , initializer: null
                                        )
                                    ))
                            )
                        )
                    , null
                    , SyntaxFactory.Token(SyntaxKind.CloseParenToken)
                    , SyntaxFactory.Block
                        (SyntaxFactory.LocalDeclarationStatement
                            (SyntaxFactory.VariableDeclaration
                                (SyntaxFactory.IdentifierName("var")
                                , SyntaxFactory.SingletonSeparatedList
                                    (SyntaxFactory.VariableDeclarator("data")
                                        .WithInitializer
                                            (SyntaxFactory.EqualsValueClause
                                                (SyntaxFactory.Token(SyntaxKind.EqualsToken)
                                                , SyntaxFactory.AwaitExpression
                                                    (SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
                                                    , SyntaxFactory.InvocationExpression
                                                        (SyntaxFactory.MemberAccessExpression
                                                            (SyntaxKind.SimpleMemberAccessExpression
                                                            , SyntaxFactory.IdentifierName("client")
                                                            , SyntaxFactory.IdentifierName("GetAsync")
                                                            )
                                                        , SyntaxFactory.ArgumentList
                                                            (SyntaxFactory.Token(SyntaxKind.OpenParenToken)
                                                            , SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("uri")))
                                                            , SyntaxFactory.Token(SyntaxKind.CloseParenToken)
                                                            )
                                                        )
                                                    )
                                                )
                                            )
                                    )
                                )
                            )
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                        , SyntaxFactory.ReturnStatement
                            (SyntaxFactory.Token(SyntaxKind.ReturnKeyword)
                            , SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("FromData"))
                                .WithArgumentList(SyntaxFactory.ArgumentList
                                    (SyntaxFactory.Token(SyntaxKind.OpenParenToken)
                                    , SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("data")))
                                    , SyntaxFactory.Token(SyntaxKind.CloseParenToken)
                                    ))
                            , SyntaxFactory.Token(SyntaxKind.SemicolonToken)
                            )
                        )
                    )));

            //public async Task<JsonProvider> LoadAsync(System.Uri uri)
            //{
            //    using (var client = new HttpClient())
            //    {
            //        var data = await client.GetStringAsync(uri);
            //        return FromData(data);
            //    }
            //}

            //public JsonProvider FromData(string data)
            //{
            //    var json = JToken.Parse(data);
            //    if (json is JObject)
            //    {
            //        return new JsonProvider
            //        {
            //            Asd = (string)json["asd"]
            //        };
            //    }
            //    throw new NotImplementedException();
            //}
        }
    }
}