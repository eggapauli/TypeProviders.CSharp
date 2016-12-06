module TypeProviders.CSharp.CodeGeneration

open ProviderImplementation.ProvidedTypes
open System
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open System.Reflection
open FSharp.Data
open TypeProviders.CSharp

module SF =
    let objectCreation typeSyntax (arguments: ArgumentSyntax list) =
        SyntaxFactory
            .ObjectCreationExpression(typeSyntax)
            .WithArgumentList(
                arguments
                |> SyntaxFactory.SeparatedList
                |> SyntaxFactory.ArgumentList
            )

    let variableDeclaration (variableName: string) initializerExpression =
        SyntaxFactory
            .VariableDeclaration(
                SyntaxFactory.IdentifierName "var"
            )
            .WithVariables(
                SyntaxFactory
                    .VariableDeclarator(variableName)
                    .WithInitializer(
                        SyntaxFactory
                            .EqualsValueClause(
                                initializerExpression
                            )
                    )
                |> SyntaxFactory.SingletonSeparatedList
            )

    let genericName (name: string) (genericArguments: TypeSyntax list) =
        SyntaxFactory
            .GenericName(name)
            .WithTypeArgumentList(
                genericArguments
                |> SyntaxFactory.SeparatedList
                |> SyntaxFactory.TypeArgumentList
            )

    let qualifiedTypeName (parts: SimpleNameSyntax list) =
        let rec impl acc p =
            match p with
            | [] -> acc
            | head :: tail ->
                let newAcc =
                    match acc with
                    | Some x -> SyntaxFactory.QualifiedName(x, head) :> NameSyntax |> Some
                    | None -> head :> NameSyntax |> Some
                impl newAcc tail
        parts
        |> impl None
        |> Option.ifNone (SyntaxFactory.IdentifierName "" :> NameSyntax)

    let simpleMemberAccess name expression = 
        SyntaxFactory
            .MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                expression,
                name
            )

    let methodInvocation memberAccess (arguments: ArgumentSyntax list) =
        SyntaxFactory
            .InvocationExpression(memberAccess)
            .WithArgumentList(
                arguments
                |> SyntaxFactory.SeparatedList
                |> SyntaxFactory.ArgumentList
            )

    let getKeywordTokenList keywords =
        keywords
        |> List.map SyntaxFactory.Token
        |> SyntaxFactory.TokenList

let private firstToLower (s: string) =
    System.Char.ToLower(s.[0]).ToString() + s.Substring 1

let private ensureIsValidIdentifier identifier =
    if SyntaxFacts.GetKeywordKind identifier = SyntaxKind.None
        && SyntaxFacts.GetContextualKeywordKind identifier = SyntaxKind.None
    then identifier
    else sprintf "@%s" identifier

let getConstructor (typeName: string) properties =
    let variableNameForProperty = firstToLower >> ensureIsValidIdentifier

    let parameters =
        properties
        |> List.map (fun (name, propertyType) ->
            SyntaxFactory
                .Parameter(
                    variableNameForProperty name
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
                    SyntaxFactory.IdentifierName(variableNameForProperty name)
                )
            )
            :> StatementSyntax
        )

    SyntaxFactory
        .ConstructorDeclaration(typeName)
        .WithModifiers(SF.getKeywordTokenList [ SyntaxKind.PublicKeyword ])
        .WithParameterList(
            parameters
            |> SyntaxFactory.SeparatedList
            |> SyntaxFactory.ParameterList
        )
        .WithBody(SyntaxFactory.Block assignmentStatements)

let private loadFromWebMethodName = "LoadAsync"
let private loadFromFileMethodName = "Load"
let private parseMethodName = "Parse"

let getLoadFromWebMethod typeSyntax (mimeType: string) =
    let returnType =
        [
            SyntaxFactory.IdentifierName "System" :> SimpleNameSyntax
            SyntaxFactory.IdentifierName "Threading" :> SimpleNameSyntax
            SyntaxFactory.IdentifierName "Tasks" :> SimpleNameSyntax
            SF.genericName "Task" [ typeSyntax ] :> SimpleNameSyntax
        ]
        |> SF.qualifiedTypeName
    let uriParam =
        SyntaxFactory
            .Parameter(SyntaxFactory.Identifier "uri")
            .WithType(SyntaxFactory.ParseTypeName "System.Uri")
    SyntaxFactory
        .MethodDeclaration(returnType, loadFromWebMethodName)
        .WithModifiers(
            [
                SyntaxKind.PublicKeyword
                SyntaxKind.StaticKeyword
                SyntaxKind.AsyncKeyword
            ]
            |> SF.getKeywordTokenList
        )
        .WithParameterList(
            uriParam
            |> SyntaxFactory.SingletonSeparatedList
            |> SyntaxFactory.ParameterList
        )
        .WithBody(
            [
                SyntaxFactory
                    .UsingStatement(
                        [
                            SF.variableDeclaration
                                "request"
                                (
                                    SF.objectCreation
                                        (SyntaxFactory.ParseTypeName "System.Net.Http.HttpRequestMessage")
                                        [
                                            SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Get") (SyntaxFactory.ParseTypeName "System.Net.Http.HttpMethod") |> SyntaxFactory.Argument
                                            uriParam.Identifier |> SyntaxFactory.IdentifierName |> SyntaxFactory.Argument
                                        ]
                                )
                            |> SyntaxFactory.LocalDeclarationStatement
                            :> StatementSyntax

                            SF.methodInvocation
                                (
                                    SyntaxFactory.IdentifierName "request"
                                    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Headers")
                                    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Accept")
                                    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Add")
                                )
                                [
                                    SF.objectCreation
                                        (SyntaxFactory.ParseTypeName "System.Net.Http.Headers.MediaTypeWithQualityHeaderValue")
                                        [
                                            SyntaxFactory
                                                .LiteralExpression(SyntaxKind.StringLiteralExpression)
                                                .WithToken(SyntaxFactory.Literal mimeType)
                                            |> SyntaxFactory.Argument
                                        ]
                                    |> SyntaxFactory.Argument
                                ]
                            |> SyntaxFactory.ExpressionStatement
                            :> StatementSyntax

                            SF.methodInvocation
                                (
                                    SyntaxFactory.IdentifierName "request"
                                    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Headers")
                                    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "UserAgent")
                                    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Add")
                                )
                                [
                                    SF.objectCreation
                                        (SyntaxFactory.ParseTypeName "System.Net.Http.Headers.ProductInfoHeaderValue")
                                        [
                                            SyntaxFactory
                                                .LiteralExpression(SyntaxKind.StringLiteralExpression)
                                                .WithToken(SyntaxFactory.Literal "TypeProviders.CSharp")
                                            |> SyntaxFactory.Argument
                                            SyntaxFactory
                                                .LiteralExpression(SyntaxKind.StringLiteralExpression)
                                                .WithToken(SyntaxFactory.Literal "0.0.1")
                                            |> SyntaxFactory.Argument
                                        ]
                                    |> SyntaxFactory.Argument
                                ]
                            |> SyntaxFactory.ExpressionStatement
                            :> StatementSyntax

                            SF.variableDeclaration
                                "response"
                                (
                                    SF.methodInvocation
                                        (
                                            SF.simpleMemberAccess
                                                (SyntaxFactory.IdentifierName "ConfigureAwait")
                                                (
                                                    SF.methodInvocation
                                                        (
                                                            SF.simpleMemberAccess
                                                                (SyntaxFactory.IdentifierName "SendAsync")
                                                                (SyntaxFactory.IdentifierName "client")
                                                        )
                                                        [ SyntaxFactory.IdentifierName "request" |> SyntaxFactory.Argument ]

                                                )
                                        )
                                        [ SyntaxFactory.LiteralExpression SyntaxKind.FalseLiteralExpression |> SyntaxFactory.Argument ]
                                    |> SyntaxFactory.AwaitExpression
                                )
                            |> SyntaxFactory.LocalDeclarationStatement
                            :> StatementSyntax

                            SF.methodInvocation
                                (
                                    SyntaxFactory.IdentifierName "response"
                                    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "EnsureSuccessStatusCode")
                                )
                                []
                            |> SyntaxFactory.ExpressionStatement
                            :> StatementSyntax

                            SF.variableDeclaration
                                "dataStream"
                                (
                                    SF.methodInvocation
                                        (
                                            SF.simpleMemberAccess
                                                (SyntaxFactory.IdentifierName "ConfigureAwait")
                                                (
                                                    SF.methodInvocation
                                                        (
                                                            SyntaxFactory.IdentifierName "response"
                                                            |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Content")
                                                            |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "ReadAsStreamAsync")
                                                        )
                                                        []

                                                )
                                        )
                                        [ SyntaxFactory.LiteralExpression SyntaxKind.FalseLiteralExpression |> SyntaxFactory.Argument ]
                                    |> SyntaxFactory.AwaitExpression
                                )
                            |> SyntaxFactory.LocalDeclarationStatement
                            :> StatementSyntax

                            SF.methodInvocation
                                (SyntaxFactory.IdentifierName parseMethodName)
                                [ SyntaxFactory.IdentifierName "dataStream" |> SyntaxFactory.Argument ]
                            |> SyntaxFactory.ReturnStatement
                            :> StatementSyntax
                        ]
                        |> SyntaxFactory.Block
                    )
                    .WithDeclaration(
                        SF.variableDeclaration
                            "client"
                            (SF.objectCreation (SyntaxFactory.ParseTypeName "System.Net.Http.HttpClient") [])
                    )
                :> StatementSyntax
            ]
            |> SyntaxFactory.Block
        )

let getLoadFromFileMethod typeSyntax =
    let filePathParam =
        SyntaxFactory
            .Parameter(SyntaxFactory.Identifier "filePath")
            .WithType(SyntaxKind.StringKeyword |> SyntaxFactory.Token |> SyntaxFactory.PredefinedType)
    SyntaxFactory
        .MethodDeclaration(typeSyntax, loadFromFileMethodName)
        .WithParameterList(
            filePathParam
            |> SyntaxFactory.SingletonSeparatedList
            |> SyntaxFactory.ParameterList
        )
        .WithModifiers(
            [
                SyntaxKind.PublicKeyword
                SyntaxKind.StaticKeyword
            ]
            |> SF.getKeywordTokenList
        )
        .WithBody(
            [
                SF.variableDeclaration
                    "data"
                    (SF.methodInvocation
                        (SF.simpleMemberAccess (SyntaxFactory.IdentifierName "ReadAllText") (SyntaxFactory.ParseTypeName "System.IO.File"))
                        [ filePathParam.Identifier |> SyntaxFactory.IdentifierName |> SyntaxFactory.Argument ]
                    )
                |> SyntaxFactory.LocalDeclarationStatement
                :> StatementSyntax

                SyntaxFactory.ReturnStatement(
                    SF.methodInvocation
                        (SyntaxFactory.IdentifierName parseMethodName)
                        [ "data" |> SyntaxFactory.IdentifierName |> SyntaxFactory.Argument ]
                )
                :> StatementSyntax
            ]
            |> SyntaxFactory.Block
        )

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
    let sampleDataStringLiteralExpression =
        SyntaxFactory
            .LiteralExpression(SyntaxKind.StringLiteralExpression)
            .WithToken(SyntaxFactory.Literal sampleData)
        :> ExpressionSyntax

    let modifiers =
        [
            SyntaxKind.PublicKeyword
            SyntaxKind.StaticKeyword
        ]
        |> SF.getKeywordTokenList

    match sampleData with
    | WebUri _ ->
        let returnType = SF.genericName "System.Threading.Tasks.Task" [ returnType ]
        SyntaxFactory
            .MethodDeclaration(returnType, "GetSampleAsync")
            .WithModifiers(modifiers)
            .WithBody(
                [
                    SF.variableDeclaration
                        "sampleUri"
                        (
                            SF.objectCreation
                                (SyntaxFactory.ParseTypeName "System.Uri")
                                [ sampleDataStringLiteralExpression |> SyntaxFactory.Argument ]
                        )
                    |> SyntaxFactory.LocalDeclarationStatement
                    :> StatementSyntax

                    SyntaxFactory
                        .ReturnStatement(
                            SF.methodInvocation
                                (SyntaxFactory.IdentifierName "LoadFromWebAsync")
                                [ SyntaxFactory.IdentifierName "sampleUri" |> SyntaxFactory.Argument ]
                        )
                    :> StatementSyntax
                ]
                |> SyntaxFactory.Block
            )
    | FileInfo _ ->
        SyntaxFactory
            .MethodDeclaration(returnType, "GetSample")
            .WithModifiers(modifiers)
            .WithBody(
                [
                    SF.variableDeclaration
                        "sampleFilePath"
                        sampleDataStringLiteralExpression
                    |> SyntaxFactory.LocalDeclarationStatement
                    :> StatementSyntax

                    SyntaxFactory
                        .ReturnStatement(
                            SF.methodInvocation
                                (SyntaxFactory.IdentifierName "LoadFromFile")
                                [ SyntaxFactory.IdentifierName "sampleFilePath" |> SyntaxFactory.Argument ]
                        )
                    :> StatementSyntax
                ]
                |> SyntaxFactory.Block
            )
    | _ ->
        SyntaxFactory
            .MethodDeclaration(returnType, "GetSample")
            .WithModifiers(modifiers)
            .WithBody(
                [
                    SF.variableDeclaration
                        "sampleData"
                        sampleDataStringLiteralExpression
                    |> SyntaxFactory.LocalDeclarationStatement
                    :> StatementSyntax

                    SyntaxFactory
                        .ReturnStatement(
                            SF.methodInvocation
                                (SyntaxFactory.IdentifierName "Parse")
                                [ SyntaxFactory.IdentifierName "sampleData" |> SyntaxFactory.Argument ]
                        )
                    :> StatementSyntax
                ]
                |> SyntaxFactory.Block
            )

let getFromStringMethod (parseStreamMethod: MethodDeclarationSyntax) returnType =
    let dataParam =
        SyntaxFactory
            .Parameter(SyntaxFactory.Identifier "data")
            .WithType(SyntaxKind.StringKeyword |> SyntaxFactory.Token |> SyntaxFactory.PredefinedType)

    SyntaxFactory
        .MethodDeclaration(returnType, SyntaxFactory.Identifier parseMethodName)
        .WithParameterList(
            dataParam
            |> SyntaxFactory.SingletonSeparatedList
            |> SyntaxFactory.ParameterList
        )
        .WithModifiers(
            [
                SyntaxKind.PublicKeyword
                SyntaxKind.StaticKeyword
            ]
            |> SF.getKeywordTokenList
        )
        .WithBody(
            [
                SF.variableDeclaration
                    "dataBytes"
                    (SF.methodInvocation
                        (
                            SyntaxFactory.ParseTypeName "System.Text.Encoding"
                            |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Default")
                            |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "GetBytes")
                        )
                        [ dataParam.Identifier |> SyntaxFactory.IdentifierName |> SyntaxFactory.Argument ]
                    )
                |> SyntaxFactory.LocalDeclarationStatement
                :> StatementSyntax

                SyntaxFactory
                    .UsingStatement(
                        SyntaxFactory
                            .ReturnStatement(
                                SF.methodInvocation
                                    (SyntaxFactory.IdentifierName parseMethodName)
                                    [ SyntaxFactory.IdentifierName "dataStream" |> SyntaxFactory.Argument ]
                            )
                    )
                    .WithDeclaration(
                        SF.variableDeclaration
                            "dataStream"
                            (
                                SF.objectCreation
                                    (SyntaxFactory.ParseTypeName "System.IO.MemoryStream")
                                    [ SyntaxFactory.IdentifierName "dataBytes" |> SyntaxFactory.Argument ]
                            )
                    )
                :> StatementSyntax
            ]
            |> SyntaxFactory.Block
        )

let getCreationMethods (rootTypeSyntax: TypeSyntax) sampleData (parseStreamStatements: TypeSyntax -> ParameterSyntax -> StatementSyntax list) mimeType =
    let parseStreamMethod =
        let parseStreamParam =
            SyntaxFactory
                .Parameter(SyntaxFactory.Identifier "dataStream")
                .WithType(SyntaxFactory.ParseTypeName "System.IO.Stream")
        SyntaxFactory
            .MethodDeclaration(rootTypeSyntax, SyntaxFactory.Identifier parseMethodName)
            .WithModifiers(
                [
                    SyntaxKind.PublicKeyword
                    SyntaxKind.StaticKeyword
                ]
                |> SF.getKeywordTokenList
            )
            .WithParameterList(
                parseStreamParam
                |> SyntaxFactory.SingletonSeparatedList
                |> SyntaxFactory.ParameterList
            )
            .WithBody(SyntaxFactory.Block(parseStreamStatements rootTypeSyntax parseStreamParam))
    [
        parseStreamMethod
        getLoadFromWebMethod rootTypeSyntax mimeType
        getLoadFromFileMethod rootTypeSyntax
        getFromStringMethod parseStreamMethod rootTypeSyntax
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

let generateCreationMethods dataType sampleData parseStreamStatements mimeType =
    let rootTypeSyntax = getTypeSyntax dataType.ReturnTypeFromParsingData
    getCreationMethods rootTypeSyntax sampleData parseStreamStatements mimeType
    |> Seq.cast<MemberDeclarationSyntax>

let generateDataStructureForMember dataTypeMember =
    let rec convertDataTypeMember dataTypeMember =
        match dataTypeMember with
        | Property (name, propertyType) ->
            SyntaxFactory
                .PropertyDeclaration(
                    getTypeSyntax propertyType,
                    SyntaxFactory.Identifier name
                )
                .WithModifiers(SF.getKeywordTokenList [ SyntaxKind.PublicKeyword ])
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
                .WithModifiers(SF.getKeywordTokenList [ SyntaxKind.PublicKeyword ])
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
    |> Seq.map generateDataStructureForMember
    |> Seq.cast<MemberDeclarationSyntax>
