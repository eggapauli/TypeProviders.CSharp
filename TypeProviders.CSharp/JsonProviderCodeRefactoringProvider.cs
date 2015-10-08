using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;
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
            try
            {
                var data = await GetData(sampleData);

                var members = GetMembers(typeDecl, data);

                var newTypeDecl = typeDecl
                    .WithMembers(List(members))
                    .WithAdditionalAnnotations(Formatter.Annotation);
                var syntaxRoot = await document.GetSyntaxRootAsync();
                return document.WithSyntaxRoot(syntaxRoot.ReplaceNode(typeDecl, newTypeDecl));
            }
            catch (Exception e)
            {
                return document;
            }
        }

        IEnumerable<MemberDeclarationSyntax> GetMembers(ClassDeclarationSyntax typeDecl, JToken data)
        {
            var members = GetPropertiesFromData(typeDecl, data);
            if (AddCreationMethods)
            {
                members = members.Concat(GetCreationMethods(typeDecl, data));
            }

            return members;
        }

        IEnumerable<MemberDeclarationSyntax> GetPropertiesFromData(TypeDeclarationSyntax typeDecl, JToken data)
        {
            var jObj = data as JObject;
            if (jObj != null)
            {
                foreach (var property in jObj.Properties())
                {
                    if (property.Value.Type == JTokenType.Object)
                    {
                        var subTypeName = typeDecl.Identifier.Text + GetIdentifierName(property.Name);
                        var subTypeDecl = ClassDeclaration(subTypeName)
                            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

                        var type = ParseTypeName(subTypeName);
                        var propertyName = GetIdentifierName(property.Name);
                        var propertyDecl = GetPropertyDeclaration(type, propertyName);
                        yield return propertyDecl;

                        var subTypeMembers = GetMembers(subTypeDecl, property.Value);
                        yield return subTypeDecl.WithMembers(List(subTypeMembers));
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
                PropertyDeclaration(type, propertyName)
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .WithAccessorList(
                        AccessorList(
                            List(
                                new[]
                                {
                                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                        .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)))
                                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                                }
                            )
                        )
                        .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
                        .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken))
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
                    return PredefinedType(Token(SyntaxKind.BoolKeyword));
                case JTokenType.Date:
                    return ParseTypeName("System.DateTime");
                case JTokenType.Float:
                    return PredefinedType(Token(SyntaxKind.DoubleKeyword));
                case JTokenType.Integer:
                    return PredefinedType(Token(SyntaxKind.IntKeyword));
                case JTokenType.String:
                    var value = (string)token;
                    Guid guid;
                    if (Guid.TryParse(value, out guid))
                        return ParseTypeName("System.Guid");
                    TimeSpan timeSpan;
                    if (TimeSpan.TryParse(value, out timeSpan))
                        return ParseTypeName("System.TimeSpan");
                    Uri uri;
                    if (Uri.TryCreate(value, UriKind.Absolute, out uri))
                        return ParseTypeName("System.Uri");

                    return PredefinedType(Token(SyntaxKind.StringKeyword));
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

        static IEnumerable<MemberDeclarationSyntax> GetCreationMethods(TypeDeclarationSyntax typeDecl, JToken sampleData)
        {
            yield return GetLoadFromUriMethod(typeDecl.Identifier.Text);
            yield return GetFromStringMethod(typeDecl.Identifier.Text);
            yield return GetFromJTokenMethod(typeDecl.Identifier.Text, sampleData);
        }

        static MethodDeclarationSyntax GetLoadFromUriMethod(string typeName)
        {
            return MethodDeclaration(ParseTypeName($"System.Threading.Tasks.Task<{typeName}>"), "LoadAsync")
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.AsyncKeyword)))
                .WithParameterList
                    (ParameterList
                        (SingletonSeparatedList
                            (Parameter(Identifier("uri")).WithType(ParseTypeName("System.Uri")))
                        )
                    )
                .WithBody
                    (Block
                        (UsingStatement
                            (Block
                                (LocalDeclarationStatement
                                    (VariableDeclaration(IdentifierName("var"))
                                        .WithVariables
                                            (SingletonSeparatedList
                                                (VariableDeclarator("data")
                                                    .WithInitializer
                                                        (EqualsValueClause
                                                            (AwaitExpression
                                                                (InvocationExpression
                                                                    (MemberAccessExpression
                                                                        (SyntaxKind.SimpleMemberAccessExpression
                                                                        , IdentifierName("client")
                                                                        , IdentifierName("GetStringAsync")
                                                                        )
                                                                    , ArgumentList(SingletonSeparatedList(Argument(IdentifierName("uri"))))
                                                                    )
                                                                )
                                                            )
                                                        )
                                                )
                                            )
                                    )
                                , ReturnStatement
                                    (InvocationExpression(IdentifierName("FromData"))
                                        .WithArgumentList
                                            (ArgumentList(SingletonSeparatedList(Argument(IdentifierName("data")))))
                                    )
                                )
                            )
                            .WithDeclaration
                                (VariableDeclaration(IdentifierName("var"))
                                    .WithVariables
                                        (SingletonSeparatedList
                                            (VariableDeclarator("client")
                                                .WithInitializer
                                                    (EqualsValueClause
                                                        (ObjectCreationExpression
                                                            (ParseTypeName("System.Net.Http.HttpClient")
                                                            , ArgumentList()
                                                            , initializer: null
                                                            )
                                                        )
                                                    )
                                            )
                                        )
                                )
                        )
                    );
        }

        static MemberDeclarationSyntax GetFromStringMethod(string typeName)
        {
            //public JsonProvider FromData(string data)
            //{
            //    var json = JToken.Parse(data);
            //    return FromData(json);
            //}
            
            return MethodDeclaration(ParseTypeName(typeName), "FromData")
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                .WithParameterList
                    (ParameterList
                        (SingletonSeparatedList
                            (Parameter(Identifier("data"))
                                .WithType(PredefinedType(Token(SyntaxKind.StringKeyword)))
                            )
                        )
                    )
                .WithBody
                    (Block
                        (LocalDeclarationStatement
                            (VariableDeclaration(IdentifierName("var"))
                                .WithVariables
                                    (SingletonSeparatedList
                                        (VariableDeclarator("json")
                                            .WithInitializer
                                                (EqualsValueClause
                                                    (InvocationExpression
                                                        (MemberAccessExpression
                                                            (SyntaxKind.SimpleMemberAccessExpression
                                                            , ParseTypeName("Newtonsoft.Json.Linq.JToken")
                                                            , IdentifierName("Parse"))
                                                        , ArgumentList(
                                                            SingletonSeparatedList(Argument(IdentifierName("data"))))
                                                        )
                                                    )
                                                )
                                        )
                                    )
                            )
                        , ReturnStatement
                            (InvocationExpression(IdentifierName("FromData"))
                                .WithArgumentList
                                    (ArgumentList(SingletonSeparatedList(Argument(IdentifierName("json")))))
                            )
                        )
                    );
        }

        static MemberDeclarationSyntax GetFromJTokenMethod(string typeName, JToken data)
        {
            //public JsonProvider FromData(JToken data)
            //{
            //    return new JsonProvider
            //    {
            //        Asd = (string)data["asd"],
            //        Obj = Obj.FromData(data["obj"])
            //    };
            //}

            var initializers = GetInitializers(typeName, data);

            return MethodDeclaration(ParseTypeName(typeName), "FromData")
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                .WithParameterList
                    (ParameterList
                        (SingletonSeparatedList
                            (Parameter(Identifier("data"))
                                .WithType(ParseTypeName("Newtonsoft.Json.Linq.JToken"))
                            )
                        )
                    )
                .WithBody
                    (Block
                        (ReturnStatement
                            (ObjectCreationExpression(IdentifierName(typeName))
                                .WithInitializer
                                    (InitializerExpression
                                        (SyntaxKind.ObjectInitializerExpression
                                        , SeparatedList(initializers)
                                        )
                                    )
                            )
                        )
                    );
        }
        
        static IEnumerable<ExpressionSyntax> GetInitializers(string typeName, JToken data)
        {
            var jObj = data as JObject;
            if (jObj != null)
            {
                foreach (var property in jObj.Properties())
                {
                    var accessExpression =
                        ElementAccessExpression
                            (IdentifierName("data")
                            , BracketedArgumentList
                                (SingletonSeparatedList
                                    (Argument
                                        (LiteralExpression
                                            (SyntaxKind.StringLiteralExpression
                                            , Literal(property.Name)
                                            )
                                        )
                                    )
                                )
                            );
                    ExpressionSyntax rightSideAssignmentExpression;
                    if (property.Value.Type == JTokenType.Object)
                    {
                        rightSideAssignmentExpression =
                            InvocationExpression
                                (MemberAccessExpression
                                    (SyntaxKind.SimpleMemberAccessExpression
                                    , IdentifierName(typeName + GetIdentifierName(property.Name))
                                    , IdentifierName("FromData")
                                    )
                                , ArgumentList
                                    (SingletonSeparatedList(Argument(accessExpression)))
                                );
                    }
                    else
                    {
                        rightSideAssignmentExpression =
                            CastExpression
                                (GetTypeFromToken(property.Value)
                                , accessExpression
                                );
                    }
                    yield return AssignmentExpression
                        (SyntaxKind.SimpleAssignmentExpression
                        , IdentifierName(GetIdentifierName(property.Name))
                        , rightSideAssignmentExpression
                        );
                }
            }
        }
    }
}