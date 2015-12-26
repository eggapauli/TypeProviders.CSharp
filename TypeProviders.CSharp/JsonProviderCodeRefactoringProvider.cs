using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

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

            var typeDecl = node as ClassDeclarationSyntax;
            if (typeDecl == null) return;

            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

            var sampleData = TypeProviderHelper.TryGetTypeProviderSampleData(typeDecl, AttributeFullName, semanticModel);
            if (!sampleData.HasValue) return;

            var action = CodeAction.Create("Synchronize type provider with sample data", c => UpdateTypeProviderAsync(context.Document, typeDecl, sampleData.Value, c));

            context.RegisterRefactoring(action);
        }

        static async Task<Document> UpdateTypeProviderAsync(Document document, ClassDeclarationSyntax typeDecl, string sampleData, CancellationToken ct)
        {
            var syntaxRoot = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            try
            {
                var data = await GetData(sampleData, ct).ConfigureAwait(false);
                var json = ParseJsonSafe(data);

                var entry = ParseData(typeDecl.Identifier.Text, json, "");

                var members = GetMembers(entry)
                    .Concat(GetCreationMethods(entry, json));

                var newTypeDecl = typeDecl
                    .WithMembers(List(members))
                    .WithAdditionalAnnotations(Formatter.Annotation);
                return document.WithSyntaxRoot(syntaxRoot.ReplaceNode(typeDecl, newTypeDecl));
            }
            catch (NotifyUserException e)
            {
                var newTypeDecl = typeDecl
                    .WithMembers(List<MemberDeclarationSyntax>())
                    .WithOpenBraceToken
                        (Token(SyntaxKind.OpenBraceToken)
                            .WithTrailingTrivia
                                (CarriageReturnLineFeed
                                , Comment($"/* {e.Message} */")
                                , CarriageReturnLineFeed)
                        );
                return document.WithSyntaxRoot(syntaxRoot.ReplaceNode(typeDecl, newTypeDecl));
            }
        }

        static async Task<string> GetData(string sampleData, CancellationToken ct)
        {
            Uri sampleDataUri;
            if (!Uri.TryCreate(sampleData, UriKind.Absolute, out sampleDataUri))
            {
                return sampleData;
            }

            if (sampleDataUri.Scheme != "http" && sampleDataUri.Scheme != "https")
            {
                throw new NotifyUserException($"Getting sample data from \"{sampleDataUri}\" is not supported. Only \"http\" and \"https\" schemes are allowed.");
            }

            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Get, sampleDataUri);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("TypeProviders.CSharp", "0.0.1"));
                var response = await client.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new NotifyUserException($"Getting sample data from \"{sampleDataUri}\" failed with status code {(int)response.StatusCode} ({response.StatusCode}).");
                }
                var data = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return data;
            }
        }

        static JToken ParseJsonSafe(string data)
        {
            try
            {
                return JToken.Parse(data);
            }
            catch (JsonReaderException e)
            {
                throw new NotifyUserException($"An error occured while parsing \"{data}\": {e.Message}", e);
            }
        }

        static HierarchicalDataEntry ParseData(string typePrefix, JToken data, string propertyName)
        {
            try
            {
                return new Match<HierarchicalDataEntry>()
                    .With((JObject x) => ParseObject(typePrefix, propertyName, x))
                    .With((JArray x) => ParseArray(typePrefix, propertyName, x))
                    .With((JValue x) => ParseValue(typePrefix, propertyName, x))
                    .Run(data);
            }
            catch (MatchException e)
            {
                throw new NotSupportedException("Unsupported JToken type: " + data.GetType().Name, e);
            }
        }

        static HierarchicalDataEntry ParseObject(string typePrefix, string propertyName, JObject data)
        {
            var subTypeName = typePrefix + propertyName.ToPublicIdentifier();
            var propertyType = ParseTypeName(subTypeName);
            var subProperties = data.Properties()
                .Select(p => ParseData(typePrefix, p.Value, p.Name.ToPublicIdentifier()));
            return new HierarchicalDataEntry
                (propertyType
                , propertyName
                , propertyType
                , subProperties
                );
        }

        static HierarchicalDataEntry ParseArray(string typePrefix, string propertyName, JArray data)
        {
            var format = "{0}";
            JToken child = data;
            while (child.Type == JTokenType.Array)
            {
                child = child[0];
                format = $"System.Collections.Generic.IReadOnlyList<{ format }>";
            }

            if (string.IsNullOrEmpty(propertyName))
            {
                var childEntry = ParseData(typePrefix, child, propertyName);
                var type = ParseTypeName(string.Format(format, childEntry.EntryType.ToString()));
                return new HierarchicalDataEntry
                    (type
                    , childEntry.PropertyName
                    , childEntry.EntryType
                    , childEntry.Children
                    );
            }
            else
            {
                var childEntry = ParseData(typePrefix, child, propertyName + "Item");
                var type = ParseTypeName(string.Format(format, childEntry.EntryType.ToString()));
                return new HierarchicalDataEntry
                    (type
                    , propertyName
                    , childEntry.EntryType
                    , childEntry.Children
                    );
            }
        }

        static HierarchicalDataEntry ParseValue(string typePrefix, string propertyName, JValue data)
        {
            var type = GetTypeFromToken(data);
            return new HierarchicalDataEntry
                (type
                , propertyName
                , type
                , Enumerable.Empty<HierarchicalDataEntry>());
        }

        static IEnumerable<MemberDeclarationSyntax> GetMembers(HierarchicalDataEntry dataEntry)
        {
            foreach (var members in GetPropertiesFromData(dataEntry))
            {
                yield return members;
            }

            if (dataEntry.Children.Count > 0)
            {
                yield return GetConstructor(dataEntry);
            }
        }

        static IEnumerable<MemberDeclarationSyntax> GetPropertiesFromData(HierarchicalDataEntry dataEntry)
        {
            foreach (var childEntry in dataEntry.Children)
            {
                yield return GetPropertyDeclaration(childEntry.PropertyType, childEntry.PropertyName);

                if (childEntry.Children.Count > 0)
                {
                    var subTypeDecl = ClassDeclaration(childEntry.EntryType.ToString())
                            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));
                    var subTypeMembers = GetMembers(childEntry);
                    yield return subTypeDecl.WithMembers(List(subTypeMembers));
                }
            }
        }

        static PropertyDeclarationSyntax GetPropertyDeclaration(TypeSyntax type, string propertyName)
        {
            return PropertyDeclaration(type, propertyName)
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                .WithAccessorList
                    (AccessorList
                        (SingletonList
                            (AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                            )
                        )
                    );
        }

        static IEnumerable<MemberDeclarationSyntax> GetCreationMethods(HierarchicalDataEntry dataEntry, JToken sampleData)
        {
            var typeName = dataEntry.PropertyType.ToString();
            yield return GetLoadFromUriMethod(typeName);
            yield return GetGetSampleMethod(typeName, sampleData);
            yield return GetFromStringMethod(typeName);
        }

        static MemberDeclarationSyntax GetConstructor(HierarchicalDataEntry dataEntry)
        {
            //[Newtonsoft.Json.JsonConstructor]
            //private JsonProvider(int propertyA, Obj propertyB)
            //{
            //    PropertyA = propertyA;
            //    PropertyB = propertyB;
            //}

            var parameters = dataEntry.Children
                .Select(c =>
                    Parameter
                        (Identifier(c.PropertyName.ToVariableIdentifier()))
                        .WithType(c.PropertyType)
                );
            var assignmentStatements = dataEntry.Children
                .Select(c =>
                    ExpressionStatement
                        (AssignmentExpression
                            (SyntaxKind.SimpleAssignmentExpression
                            , IdentifierName(c.PropertyName)
                            , IdentifierName(c.PropertyName.ToVariableIdentifier())
                            )
                        )
                );

            return ConstructorDeclaration(dataEntry.EntryType.ToString())
                .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)))
                .WithAttributeLists
                    (List
                        (new[]
                            {
                                AttributeList
                                    (SingletonSeparatedList
                                        (Attribute
                                            (ParseName("Newtonsoft.Json.JsonConstructor"))
                                        )
                                    )
                            }
                        )
                    )
                .WithParameterList(ParameterList(SeparatedList(parameters)))
                .WithBody(Block(assignmentStatements));
        }

        static MethodDeclarationSyntax GetLoadFromUriMethod(string typeName)
        {
            //public async System.Threading.Tasks.Task<JsonProvider> LoadAsync(System.Uri uri)
            //{
            //    using (var client = new System.Net.Http.HttpClient())
            //    {
            //        var request = new System.Net.Http.HttpRequestMessage(System.Net.HttpMethod.Get, uri);
            //        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            //        request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("TypeProviders.CSharp", "0.0.1"));
            //        var response = await client.SendAsync(request).ConfigureAwait(false);
            //        response.EnsureSuccessStatusCode();
            //        var data = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            //        return FromData(data);
            //    }
            //}

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
                                                (VariableDeclarator("request")
                                                    .WithInitializer
                                                        (EqualsValueClause
                                                            (ObjectCreationExpression
                                                                (ParseTypeName("System.Net.Http.HttpRequestMessage"))
                                                                .WithArgumentList
                                                                    (ArgumentList
                                                                        (SeparatedList
                                                                            (new[]
                                                                                {
                                                                                    Argument
                                                                                        (MemberAccessExpression
                                                                                            (SyntaxKind.SimpleMemberAccessExpression
                                                                                            , ParseTypeName("System.Net.Http.HttpMethod")
                                                                                            , IdentifierName("Get")
                                                                                            )
                                                                                        )
                                                                                    , Argument(IdentifierName("uri"))
                                                                                }
                                                                            )
                                                                        )
                                                                    )
                                                            )
                                                        )
                                                )
                                            )
                                    )
                                , ExpressionStatement
                                    (InvocationExpression
                                        (MemberAccessExpression
                                            (SyntaxKind.SimpleMemberAccessExpression
                                            , MemberAccessExpression
                                                (SyntaxKind.SimpleMemberAccessExpression
                                                , MemberAccessExpression
                                                    (SyntaxKind.SimpleMemberAccessExpression
                                                    , IdentifierName("request")
                                                    , IdentifierName("Headers")
                                                    )
                                                , IdentifierName("Accept")
                                                )
                                            , IdentifierName("Add")
                                            )
                                        )
                                        .WithArgumentList
                                            (ArgumentList
                                                (SingletonSeparatedList
                                                    (Argument
                                                        (ObjectCreationExpression
                                                            (ParseTypeName("System.Net.Http.Headers.MediaTypeWithQualityHeaderValue"))
                                                            .WithArgumentList
                                                                (ArgumentList
                                                                    (SingletonSeparatedList
                                                                        (Argument
                                                                            (LiteralExpression
                                                                                (SyntaxKind.StringLiteralExpression)
                                                                                .WithToken(Literal("application/json"))
                                                                            )
                                                                        )
                                                                    )
                                                                )
                                                        )
                                                    )
                                                )
                                            )
                                    )
                                , ExpressionStatement
                                    (InvocationExpression
                                        (MemberAccessExpression
                                            (SyntaxKind.SimpleMemberAccessExpression
                                            , MemberAccessExpression
                                                (SyntaxKind.SimpleMemberAccessExpression
                                                , MemberAccessExpression
                                                    (SyntaxKind.SimpleMemberAccessExpression
                                                    , IdentifierName("request")
                                                    , IdentifierName("Headers")
                                                    )
                                                , IdentifierName("UserAgent")
                                                )
                                            , IdentifierName("Add")
                                            )
                                        )
                                        .WithArgumentList
                                            (ArgumentList
                                                (SingletonSeparatedList
                                                    (Argument
                                                        (ObjectCreationExpression
                                                            (ParseTypeName("System.Net.Http.Headers.ProductInfoHeaderValue"))
                                                            .WithArgumentList
                                                                (ArgumentList
                                                                    (SeparatedList
                                                                        (new[]
                                                                            {
                                                                                Argument
                                                                                    (LiteralExpression
                                                                                        (SyntaxKind.StringLiteralExpression)
                                                                                        .WithToken(Literal("TypeProviders.CSharp"))
                                                                                    )
                                                                                , Argument
                                                                                    (LiteralExpression
                                                                                        (SyntaxKind.StringLiteralExpression)
                                                                                        .WithToken(Literal("0.0.1"))
                                                                                    )
                                                                            }
                                                                        )
                                                                    )
                                                                )
                                                        )
                                                    )
                                                )
                                            )
                                    )
                                , LocalDeclarationStatement
                                    (VariableDeclaration(IdentifierName("var"))
                                        .WithVariables
                                            (SingletonSeparatedList
                                                (VariableDeclarator(Identifier("response"))
                                                    .WithInitializer
                                                        (EqualsValueClause
                                                            (AwaitExpression
                                                                (InvocationExpression
                                                                    (MemberAccessExpression
                                                                        (SyntaxKind.SimpleMemberAccessExpression
                                                                        , InvocationExpression
                                                                            (MemberAccessExpression
                                                                                (SyntaxKind.SimpleMemberAccessExpression
                                                                                , IdentifierName("client")
                                                                                , IdentifierName("SendAsync")
                                                                                )
                                                                            )
                                                                            .WithArgumentList
                                                                                (ArgumentList
                                                                                    (SingletonSeparatedList
                                                                                        (Argument(IdentifierName("request")))
                                                                                    )
                                                                                )
                                                                        , IdentifierName("ConfigureAwait")
                                                                        )
                                                                    )
                                                                    .WithArgumentList
                                                                        (ArgumentList
                                                                            (SingletonSeparatedList
                                                                                (Argument
                                                                                    (LiteralExpression
                                                                                        (SyntaxKind.FalseLiteralExpression
                                                                                        , Token(SyntaxKind.FalseKeyword)
                                                                                        )
                                                                                    )
                                                                                )
                                                                            )
                                                                        )
                                                                )
                                                            )
                                                        )
                                                )
                                            )
                                    )
                                , ExpressionStatement
                                    (InvocationExpression
                                        (MemberAccessExpression
                                            (SyntaxKind.SimpleMemberAccessExpression
                                            , IdentifierName("response")
                                            , IdentifierName("EnsureSuccessStatusCode")
                                            )
                                        )
                                    )
                                , LocalDeclarationStatement
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
                                                                        , InvocationExpression
                                                                            (MemberAccessExpression
                                                                                (SyntaxKind.SimpleMemberAccessExpression
                                                                                , MemberAccessExpression
                                                                                    (SyntaxKind.SimpleMemberAccessExpression
                                                                                    , IdentifierName("response")
                                                                                    , IdentifierName("Content")
                                                                                    )
                                                                                , IdentifierName("ReadAsStringAsync")
                                                                                )
                                                                            )
                                                                        , IdentifierName("ConfigureAwait")
                                                                        )
                                                                    )
                                                                    .WithArgumentList
                                                                        (ArgumentList
                                                                            (SingletonSeparatedList
                                                                                (Argument
                                                                                    (LiteralExpression
                                                                                        (SyntaxKind.FalseLiteralExpression
                                                                                        , Token(SyntaxKind.FalseKeyword)
                                                                                        )
                                                                                    )
                                                                                )
                                                                            )
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
            //    return Newtonsoft.Json.JsonConvert.DeserializeObject<JsonProvider>(data);
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
                        (ReturnStatement
                            (InvocationExpression
                                (MemberAccessExpression
                                    (SyntaxKind.SimpleMemberAccessExpression
                                    , ParseTypeName("Newtonsoft.Json.JsonConvert")
                                    , GenericName("DeserializeObject")
                                        .WithTypeArgumentList
                                            (TypeArgumentList
                                                (SingletonSeparatedList(ParseTypeName(typeName)))
                                            )
                                    )
                                )
                                .WithArgumentList
                                    (ArgumentList(SingletonSeparatedList(Argument(IdentifierName("data")))))
                            )
                        )
                    );
        }

        static MethodDeclarationSyntax GetGetSampleMethod(string typeName, JToken sampleData)
        {
            //public JsonProvider GetSample()
            //{
            //    var data = "...";
            //    return FromData(data);
            //}

            return MethodDeclaration(ParseTypeName(typeName), "GetSample")
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                .WithBody
                    (Block
                        (LocalDeclarationStatement
                            (VariableDeclaration(IdentifierName("var"))
                                .WithVariables
                                    (SingletonSeparatedList
                                        (VariableDeclarator("data")
                                            .WithInitializer
                                                (EqualsValueClause
                                                    (LiteralExpression
                                                        (SyntaxKind.StringLiteralExpression)
                                                        .WithToken
                                                            (Literal
                                                                (sampleData.ToString
                                                                    (Newtonsoft.Json.Formatting.None)
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
                    );
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
    }
}