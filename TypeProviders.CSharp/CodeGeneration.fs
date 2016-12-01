module TypeProviders.CSharp.CodeGeneration

open ProviderImplementation.ProvidedTypes
open System
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open System.Reflection
open FSharp.Data
open TypeProviders.CSharp

let private parseMethodDeclarationSyntax =
    String.concat Environment.NewLine
    >> SyntaxFactory.ParseSyntaxTree
    >> fun t ->
        t.GetRoot().ChildNodes()
        |> Seq.exactlyOne
        :?> MethodDeclarationSyntax
        |> fun node -> node.WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed, SyntaxFactory.CarriageReturnLineFeed)

let private indent i s =
    (String.replicate (4 * i) " ") + s

let private firstToLower (s: string) =
    System.Char.ToLower(s.[0]).ToString() + s.Substring 1

let getConstructor (typeName: string) properties =
    let parameters =
        properties
        |> List.map (fun (name, propertyType) ->
            SyntaxFactory
                .Parameter(
                    firstToLower name
                    |> SyntaxFactory.Identifier
                )
                .WithType(propertyType)
        )

    let assignmentStatements =
        properties
        |> List.map (fst >> fun name ->
            SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName name,
                    SyntaxFactory.IdentifierName(firstToLower name)
                )
            )
            :> StatementSyntax
        )

    SyntaxFactory
        .ConstructorDeclaration(typeName)
        .WithModifiers(
            SyntaxKind.PublicKeyword
            |> SyntaxFactory.Token
            |> SyntaxFactory.TokenList
        )
        .WithParameterList(
            parameters
            |> SyntaxFactory.SeparatedList
            |> SyntaxFactory.ParameterList
        )
        .WithBody(SyntaxFactory.Block assignmentStatements)

let private loadFromWebMethodName = "LoadAsync"
let private loadFromFileMethodName = "Load"
let private parseMethodName = "Parse"

let getLoadFromWebMethod typeSyntax =
    [
        sprintf "public static async System.Threading.Tasks.Task<%O> %s(System.Uri uri)" typeSyntax loadFromWebMethodName
        "{"
        "    using (var client = new System.Net.Http.HttpClient())"
        "    {"
        "        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);"
        "        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(\"application/json\"));"
        "        request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue(\"TypeProviders.CSharp\", \"0.0.1\"));"
        "        var response = await client.SendAsync(request).ConfigureAwait(false);"
        "        response.EnsureSuccessStatusCode();"
        "        var data = await response.Content.ReadAsStringAsync().ConfigureAwait(false);"
        sprintf "        return %s(data);" parseMethodName
        "    }"
        "}"
    ]
    |> parseMethodDeclarationSyntax

let getLoadFromFileMethod typeSyntax =
    [
        sprintf "public static %O %s(string filePath)" typeSyntax loadFromFileMethodName
        "{"
        "    var data = System.IO.File.ReadAllText(filePath);"
        sprintf "    return %s(data);" parseMethodName
        "}"
    ]
    |> parseMethodDeclarationSyntax

let toUri str =
    match Uri.TryCreate(str, UriKind.RelativeOrAbsolute) with
    | true, uri -> Some uri
    | _ -> None

let private (|WebUri|_|) str =
    let isWeb (uri: System.Uri) =
        uri.IsAbsoluteUri && not uri.IsUnc && uri.Scheme <> "file"

    match toUri str with
    | Some uri when isWeb uri -> Some uri
    | _ -> None

let private (|FileInfo|_|) str =
    let isValidFileName str =
        let isValidPathChar ch =
             System.IO.Path.GetInvalidPathChars()
             |> Array.contains ch
             |> not
        str
        |> Seq.forall isValidPathChar
    match toUri str with
    | Some _  when isValidFileName str -> System.IO.FileInfo str |> Some
    | _ -> None

let getGetSampleMethod returnType (sampleData: string) =
    //public static JsonProvider GetSample()
    //{
    //    var data = "...";
    //    return Parse(data);
    //}

    // OR:

    //public static Task<JsonProvider> GetSampleAsync()
    //{
    //    var data = new Uri("...");
    //    return LoadFromWebAsync(data);
    //}

    // OR:

    //public static JsonProvider GetSample()
    //{
    //    var data = "...";
    //    return LoadFromFile(data);
    //}

    let isWebUri, isFilePath, loadMethodName =
        match sampleData with
        | WebUri _ -> true, false, loadFromWebMethodName
        | FileInfo _ -> false, true, loadFromFileMethodName
        | _ -> false, false, parseMethodName

    let isAsync = isWebUri

    let returnType =
        if isAsync
        then
            SyntaxFactory.GenericName(
                SyntaxFactory.Identifier "System.Threading.Tasks.Task",
                returnType |> SyntaxFactory.SingletonSeparatedList |> SyntaxFactory.TypeArgumentList
            ) :> TypeSyntax
        else returnType

    let sampleDataExpression =
        let stringLiteralExpression =
            SyntaxFactory
                .LiteralExpression(SyntaxKind.StringLiteralExpression)
                .WithToken(SyntaxFactory.Literal sampleData)
            :> ExpressionSyntax

        if isWebUri
        then
            SyntaxFactory
                .ObjectCreationExpression(
                    SyntaxFactory.ParseTypeName "System.Uri"
                )
                .WithArgumentList(
                    stringLiteralExpression
                    |> SyntaxFactory.Argument
                    |> SyntaxFactory.SingletonSeparatedList
                    |> SyntaxFactory.ArgumentList
                )
                :> ExpressionSyntax
        else stringLiteralExpression

    SyntaxFactory
        .MethodDeclaration(returnType, if isAsync then "GetSampleAsync" else "GetSample")
        .WithModifiers(
            [
                SyntaxKind.PublicKeyword
                SyntaxKind.StaticKeyword
            ]
            |> List.map SyntaxFactory.Token
            |> SyntaxFactory.TokenList
        )
        .WithBody(
            SyntaxFactory.Block(
                SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory
                        .VariableDeclaration(SyntaxFactory.IdentifierName "var")
                        .WithVariables(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory
                                    .VariableDeclarator("data")
                                    .WithInitializer(
                                        SyntaxFactory.EqualsValueClause sampleDataExpression
                                    )
                                )
                            )
                ),
                SyntaxFactory
                    .ReturnStatement(
                        SyntaxFactory
                            .InvocationExpression(
                                SyntaxFactory.IdentifierName loadMethodName
                            )
                            .WithArgumentList(
                                SyntaxFactory.IdentifierName "data"
                                |> SyntaxFactory.Argument
                                |> SyntaxFactory.SingletonSeparatedList
                                |> SyntaxFactory.ArgumentList
                            )
                    )
            )
       )

let getFromStringMethod returnType =
    SyntaxFactory
        .MethodDeclaration(returnType, parseMethodName)
        .WithModifiers(
            SyntaxFactory.TokenList(
                SyntaxFactory.Token SyntaxKind.PublicKeyword,
                SyntaxFactory.Token SyntaxKind.StaticKeyword
            )
        )
        .WithParameterList(
            SyntaxFactory
                .Parameter(SyntaxFactory.Identifier "data")
                .WithType(
                    SyntaxKind.StringKeyword
                    |> SyntaxFactory.Token
                    |> SyntaxFactory.PredefinedType
                )
            |> SyntaxFactory.SingletonSeparatedList
            |> SyntaxFactory.ParameterList
        )
        .WithBody(
            SyntaxFactory.Block(
                SyntaxFactory.ReturnStatement(
                    SyntaxFactory
                        .InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.ParseTypeName "Newtonsoft.Json.JsonConvert",
                                SyntaxFactory
                                    .GenericName("DeserializeObject")
                                    .WithTypeArgumentList(
                                        returnType
                                        |> SyntaxFactory.SingletonSeparatedList
                                        |> SyntaxFactory.TypeArgumentList
                                    )
                            )
                        )
                        .WithArgumentList(
                            SyntaxFactory.IdentifierName "data"
                            |> SyntaxFactory.Argument
                            |> SyntaxFactory.SingletonSeparatedList
                            |> SyntaxFactory.ArgumentList
                        )
                )
            )
        )

let getCreationMethods (rootTypeSyntax: TypeSyntax) sampleData =
    [
        getLoadFromWebMethod rootTypeSyntax
        getLoadFromFileMethod rootTypeSyntax
        getFromStringMethod rootTypeSyntax
        getGetSampleMethod rootTypeSyntax sampleData
    ]

let toSyntaxKind = function
    | TBool -> SyntaxKind.BoolKeyword
    | TByte -> SyntaxKind.ByteKeyword
    | TSByte -> SyntaxKind.SByteKeyword
    | TChar -> SyntaxKind.CharKeyword
    | TDecimal -> SyntaxKind.DecimalKeyword
    | TDouble -> SyntaxKind.DoubleKeyword
    | TFloat -> SyntaxKind.FloatKeyword
    | TInt -> SyntaxKind.IntKeyword
    | TUInt -> SyntaxKind.UIntKeyword
    | TLong -> SyntaxKind.LongKeyword
    | TULong -> SyntaxKind.ULongKeyword
    | TObject -> SyntaxKind.ObjectKeyword
    | TShort -> SyntaxKind.ShortKeyword
    | TUShort -> SyntaxKind.UShortKeyword
    | TString -> SyntaxKind.StringKeyword

let rec getTypeSyntax = function
    | Common s -> SyntaxFactory.ParseTypeName s
    | Collection s ->
        SyntaxFactory.QualifiedName(
            SyntaxFactory.QualifiedName(
                SyntaxFactory.QualifiedName(
                    SyntaxFactory.IdentifierName "System",
                    SyntaxFactory.IdentifierName "Collections"
                ),
                SyntaxFactory.IdentifierName "Generic"
            ),
            SyntaxFactory.GenericName(
                SyntaxFactory.Identifier "IReadOnlyList",
                getTypeSyntax s
                |> SyntaxFactory.SingletonSeparatedList
                |> SyntaxFactory.TypeArgumentList
            )
        )
        :> TypeSyntax
    | Predefined csType ->
        csType
        |> toSyntaxKind
        |> SyntaxFactory.Token
        |> SyntaxFactory.PredefinedType
        :> TypeSyntax
    | Optional t ->
        getTypeSyntax t
        |> SyntaxFactory.NullableType
        :> TypeSyntax

let generateCreationMethods dataType sampleData =
    let rootTypeSyntax = getTypeSyntax dataType.ReturnTypeFromParsingData
    getCreationMethods rootTypeSyntax sampleData
    |> Seq.cast<MemberDeclarationSyntax>

let generateJsonType dataTypeMember =
    let rec convertDataTypeMember dataTypeMember =
        match dataTypeMember with
        | Property (name, propertyType) ->
            SyntaxFactory
                .PropertyDeclaration(
                    getTypeSyntax propertyType,
                    SyntaxFactory.Identifier name
                )
                .WithModifiers(
                    SyntaxKind.PublicKeyword
                    |> SyntaxFactory.Token
                    |> SyntaxFactory.TokenList
                )
                .WithAccessorList(
                    SyntaxFactory.AccessorList(
                        SyntaxFactory.SingletonList(
                            SyntaxFactory
                                .AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithSemicolonToken(
                                    SyntaxKind.SemicolonToken
                                    |> SyntaxFactory.Token
                                )
                        )
                    )
                )
            :> MemberDeclarationSyntax
        | SubType (name, members) ->
            let properties =
                members
                |> List.choose (function
                    | Property (name, propertyType) ->
                        Some (name, getTypeSyntax propertyType)
                    | _ -> None
                )

            SyntaxFactory
                .ClassDeclaration(SyntaxFactory.Identifier name)
                .WithModifiers(
                    SyntaxKind.PublicKeyword
                    |> SyntaxFactory.Token
                    |> SyntaxFactory.TokenList
                )
                .WithMembers(
                    [
                        yield! members |> List.map convertDataTypeMember
                        yield getConstructor name properties :> MemberDeclarationSyntax
                    ]
                    |> SyntaxFactory.List
                )
                :> MemberDeclarationSyntax

    convertDataTypeMember dataTypeMember :?> TypeDeclarationSyntax

let generateDataStructure dataType =
    dataType.Members
    |> Seq.map generateJsonType
    |> Seq.cast<MemberDeclarationSyntax>
