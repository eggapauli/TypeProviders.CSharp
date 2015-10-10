using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
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

            var dataEntries = ParseData(typeDecl.Identifier.Text, data).ToList();
            var typeName = ParseTypeName(typeDecl.Identifier.Text);
            var rootEntry = new HierarchicalDataEntry(typeName, typeName.ToString(), typeName, dataEntries);

            var members = GetMembers(rootEntry)
                .Concat(GetCreationMethods(rootEntry, data));

            var newTypeDecl = typeDecl
                .WithMembers(List(members))
                .WithAdditionalAnnotations(Formatter.Annotation);
            var syntaxRoot = await document.GetSyntaxRootAsync();
            return document.WithSyntaxRoot(syntaxRoot.ReplaceNode(typeDecl, newTypeDecl));
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

        static IEnumerable<HierarchicalDataEntry> ParseData(string typePrefix, JToken data)
        {
            var jObj = data as JObject;
            if (jObj != null)
            {
                foreach (var property in jObj.Properties())
                {
                    if (property.Value.Type == JTokenType.Object)
                    {
                        var subTypeName = typePrefix + GetIdentifierName(property.Name);
                        var propertyType = ParseTypeName(subTypeName);
                        var propertyName = GetIdentifierName(property.Name);
                        var subProperties = ParseData(typePrefix, property.Value);
                        yield return new HierarchicalDataEntry(propertyType, propertyName, propertyType, subProperties);
                    }
                    else if (property.Value.Type == JTokenType.Array)
                    {
                        var format = "{0}";
                        var child = property.Value;
                        while (child.Type == JTokenType.Array)
                        {
                            child = child[0];
                            format = $"System.Collections.Generic.IReadOnlyList<{ format }>";
                        }

                        var type = child.Type == JTokenType.Object
                            ? ParseTypeName(typePrefix + GetIdentifierName(property.Name) + "Item")
                            : GetTypeFromToken(child);

                        var propertyType = ParseTypeName(string.Format(format, type.ToString()));
                        var propertyName = GetIdentifierName(property.Name);
                        var subProperties = ParseData(typePrefix, child);
                        yield return new HierarchicalDataEntry(propertyType, propertyName, type, subProperties);
                    }
                    else
                    {
                        var type = GetTypeFromToken(property.Value);
                        var propertyName = GetIdentifierName(property.Name);
                        yield return new HierarchicalDataEntry(type, propertyName);
                    }
                }
            }
        }

        IEnumerable<MemberDeclarationSyntax> GetMembers(HierarchicalDataEntry dataEntry)
        {
            foreach (var members in GetPropertiesFromData(dataEntry.Children))
            {
                yield return members;
            }
            yield return GetConstructor(dataEntry);
        }

        IEnumerable<MemberDeclarationSyntax> GetPropertiesFromData(IReadOnlyCollection<HierarchicalDataEntry> dataEntries)
        {
            foreach (var dataEntry in dataEntries)
            {
                yield return GetPropertyDeclaration(dataEntry.PropertyType, dataEntry.PropertyName);

                if (dataEntry.Children.Count > 0)
                {
                    var subTypeDecl = ClassDeclaration(dataEntry.EntryType.ToString())
                        .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));
                    var subTypeMembers = GetMembers(dataEntry);
                    yield return subTypeDecl.WithMembers(List(subTypeMembers));
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
                    );
        }

        static IEnumerable<MemberDeclarationSyntax> GetCreationMethods(HierarchicalDataEntry dataEntry, JToken sampleData)
        {
            yield return GetLoadFromUriMethod(dataEntry.EntryType.ToString());
            yield return GetGetSampleMethod(dataEntry.EntryType.ToString(), sampleData);
            yield return GetFromStringMethod(dataEntry.EntryType.ToString());
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
                        (Identifier(c.VariableName))
                        .WithType(c.PropertyType)
                );
            var assignmentStatements = dataEntry.Children
                .Select(c =>
                    ExpressionStatement
                        (AssignmentExpression
                            (SyntaxKind.SimpleAssignmentExpression
                            , IdentifierName(c.PropertyName)
                            , IdentifierName(c.VariableName)
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
                                            (IdentifierName("Newtonsoft.Json.JsonConstructor"))
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
            //public async System.Threading.Tasks.Task<JsonProvider> FromData(System.Uri uri)
            //{
            //    using (var client = new System.Net.Http.HttpClient())
            //    {
            //        var data = await client.GetStringAsync(uri);
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
            //    return Newtonsoft.Json.JsonConvert.DeserializeObject<JsonProvider>(data);
            //}

            return MethodDeclaration(IdentifierName(typeName), "FromData")
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
                                    , IdentifierName("Newtonsoft.Json.JsonConvert")
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

            return MethodDeclaration(IdentifierName(typeName), "GetSample")
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
    }
}