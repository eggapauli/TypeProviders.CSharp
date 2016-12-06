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
    let objectCreation (arguments: ArgumentSyntax list) typeSyntax =
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
        let folder state item =
            match state with
            | Some s -> SyntaxFactory.QualifiedName(s, item) :> NameSyntax |> Some
            | None -> item :> NameSyntax |> Some

        List.fold folder None parts
        |> Option.ifNone (SyntaxFactory.IdentifierName "" :> NameSyntax)

    let simpleMemberAccess name expression = 
        SyntaxFactory
            .MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                expression,
                name
            )

    let methodInvocation (arguments: ArgumentSyntax list) memberAccess =
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

    let parameter name parameterType =
        SyntaxFactory
            .Parameter(SyntaxFactory.Identifier name)
            .WithType(parameterType)

    let singletonParameter =
        SyntaxFactory.SingletonSeparatedList
        >> SyntaxFactory.ParameterList

    let falseArg =
        SyntaxKind.FalseLiteralExpression
        |> SyntaxFactory.LiteralExpression
        |> SyntaxFactory.Argument

    let stringLiteral (value: string) =
        SyntaxFactory
            .LiteralExpression(SyntaxKind.StringLiteralExpression)
            .WithToken(SyntaxFactory.Literal value)

let private firstToLower (s: string) =
    System.Char.ToLower(s.[0]).ToString() + s.Substring 1

let private ensureIsValidIdentifier identifier =
    if SyntaxFacts.GetKeywordKind identifier = SyntaxKind.None
        && SyntaxFacts.GetContextualKeywordKind identifier = SyntaxKind.None
    then identifier
    else sprintf "@%s" identifier

let getConstructor (typeName: string) properties =
    let variableNameForProperty = firstToLower >> ensureIsValidIdentifier

    SyntaxFactory
        .ConstructorDeclaration(typeName)
        .WithModifiers(SF.getKeywordTokenList [ SyntaxKind.PublicKeyword ])
        .WithParameterList(
            properties
            |> List.map (fun (name, propertyType) ->
                SF.parameter (variableNameForProperty name) propertyType
            )
            |> SyntaxFactory.SeparatedList
            |> SyntaxFactory.ParameterList
        )
        .WithBody(
            properties
            |> List.map (fst >> fun name ->
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName name,
                    SyntaxFactory.IdentifierName(variableNameForProperty name)
                )
                |> SyntaxFactory.ExpressionStatement
                :> StatementSyntax
            )
            |> SyntaxFactory.Block
        )

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

    let uriParam = SF.parameter "uri" (SyntaxFactory.ParseTypeName "System.Uri")

    let createRequestObject =
        let httpMethodGetArg =
            SyntaxFactory.ParseTypeName "System.Net.Http.HttpMethod"
            |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Get")
            |> SyntaxFactory.Argument

        let uriArg =
            uriParam.Identifier
            |> SyntaxFactory.IdentifierName
            |> SyntaxFactory.Argument

        SyntaxFactory.ParseTypeName "System.Net.Http.HttpRequestMessage"
        |> SF.objectCreation [ httpMethodGetArg; uriArg ]
        |> SF.variableDeclaration "request"
        |> SyntaxFactory.LocalDeclarationStatement
        :> StatementSyntax

    let setAcceptHeader =
        let mimeTypeArg =
            SyntaxFactory.ParseTypeName "System.Net.Http.Headers.MediaTypeWithQualityHeaderValue"
            |> SF.objectCreation
                [
                    SF.stringLiteral mimeType |> SyntaxFactory.Argument
                ]
            |> SyntaxFactory.Argument

        SyntaxFactory.IdentifierName "request"
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Headers")
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Accept")
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Add")
        |> SF.methodInvocation [ mimeTypeArg ]
        |> SyntaxFactory.ExpressionStatement
        :> StatementSyntax

    let setUserAgentHeader =
        let userAgentArg =
            SyntaxFactory.ParseTypeName "System.Net.Http.Headers.ProductInfoHeaderValue"
            |> SF.objectCreation
                [
                    SF.stringLiteral "TypeProviders.CSharp" |> SyntaxFactory.Argument
                    SF.stringLiteral "0.0.1" |> SyntaxFactory.Argument
                ]
            |> SyntaxFactory.Argument

        SyntaxFactory.IdentifierName "request"
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Headers")
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "UserAgent")
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Add")
        |> SF.methodInvocation [ userAgentArg ]
        |> SyntaxFactory.ExpressionStatement
        :> StatementSyntax

    let sendRequest =
        

        (SyntaxFactory.IdentifierName "client")
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "SendAsync")
        |> SF.methodInvocation [ SyntaxFactory.IdentifierName "request" |> SyntaxFactory.Argument ]
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "ConfigureAwait")
        |> SF.methodInvocation [ SF.falseArg ]
        |> SyntaxFactory.AwaitExpression
        |> SF.variableDeclaration "response"
        |> SyntaxFactory.LocalDeclarationStatement
        :> StatementSyntax

    let throwIfErrorResponseStatus =
        SyntaxFactory.IdentifierName "response"
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "EnsureSuccessStatusCode")
        |> SF.methodInvocation []
        |> SyntaxFactory.ExpressionStatement
        :> StatementSyntax

    let readResponseStream =
        SyntaxFactory.IdentifierName "response"
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Content")
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "ReadAsStreamAsync")
        |> SF.methodInvocation []
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "ConfigureAwait")
        |> SF.methodInvocation [ SF.falseArg ]
        |> SyntaxFactory.AwaitExpression
        |> SF.variableDeclaration "dataStream"
        |> SyntaxFactory.LocalDeclarationStatement
        :> StatementSyntax

    let parseStreamAndReturn =
        SyntaxFactory.IdentifierName parseMethodName
        |> SF.methodInvocation [ SyntaxFactory.IdentifierName "dataStream" |> SyntaxFactory.Argument ]
        |> SyntaxFactory.ReturnStatement
        :> StatementSyntax

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
        .WithParameterList(SF.singletonParameter uriParam)
        .WithBody(
            [
                SyntaxFactory
                    .UsingStatement(
                        [
                            createRequestObject
                            setAcceptHeader
                            setUserAgentHeader
                            sendRequest
                            throwIfErrorResponseStatus
                            readResponseStream
                            parseStreamAndReturn
                        ]
                        |> SyntaxFactory.Block
                    )
                    .WithDeclaration(
                        SyntaxFactory.ParseTypeName "System.Net.Http.HttpClient"
                        |> SF.objectCreation []
                        |> SF.variableDeclaration "client"
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
                SyntaxFactory.ParseTypeName "System.IO.File"
                |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "ReadAllText")
                |> SF.methodInvocation [ filePathParam.Identifier |> SyntaxFactory.IdentifierName |> SyntaxFactory.Argument ]
                |> SF.variableDeclaration "data"
                |> SyntaxFactory.LocalDeclarationStatement
                :> StatementSyntax

                SyntaxFactory.IdentifierName parseMethodName
                |> SF.methodInvocation [ "data" |> SyntaxFactory.IdentifierName |> SyntaxFactory.Argument ]
                |> SyntaxFactory.ReturnStatement
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
    let sampleDataStringLiteralExpression = SF.stringLiteral sampleData

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
                    SyntaxFactory.ParseTypeName "System.Uri"
                    |> SF.objectCreation [ sampleDataStringLiteralExpression |> SyntaxFactory.Argument ]
                    |> SF.variableDeclaration "sampleUri"
                    |> SyntaxFactory.LocalDeclarationStatement
                    :> StatementSyntax

                    SyntaxFactory.IdentifierName "LoadFromWebAsync"
                    |> SF.methodInvocation  [ SyntaxFactory.IdentifierName "sampleUri" |> SyntaxFactory.Argument ]
                    |> SyntaxFactory.ReturnStatement
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
                    sampleDataStringLiteralExpression
                    |> SF.variableDeclaration "sampleFilePath"
                    |> SyntaxFactory.LocalDeclarationStatement
                    :> StatementSyntax

                    SyntaxFactory.IdentifierName "LoadFromFile"
                    |> SF.methodInvocation [ SyntaxFactory.IdentifierName "sampleFilePath" |> SyntaxFactory.Argument ]
                    |> SyntaxFactory.ReturnStatement
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
                    sampleDataStringLiteralExpression
                    |> SF.variableDeclaration "sampleData"
                    |> SyntaxFactory.LocalDeclarationStatement
                    :> StatementSyntax

                    SyntaxFactory.IdentifierName "Parse"
                    |> SF.methodInvocation [ SyntaxFactory.IdentifierName "sampleData" |> SyntaxFactory.Argument ]
                    |> SyntaxFactory.ReturnStatement
                    :> StatementSyntax
                ]
                |> SyntaxFactory.Block
            )

let getFromStringMethod (parseStreamMethod: MethodDeclarationSyntax) returnType =
    let dataParam =
        SyntaxKind.StringKeyword
        |> SyntaxFactory.Token
        |> SyntaxFactory.PredefinedType
        |> SF.parameter "data"

    SyntaxFactory
        .MethodDeclaration(returnType, SyntaxFactory.Identifier parseMethodName)
        .WithParameterList(SF.singletonParameter dataParam)
        .WithModifiers(
            [
                SyntaxKind.PublicKeyword
                SyntaxKind.StaticKeyword
            ]
            |> SF.getKeywordTokenList
        )
        .WithBody(
            [
                SyntaxFactory.ParseTypeName "System.Text.Encoding"
                |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Default")
                |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "GetBytes")
                |> SF.methodInvocation [ dataParam.Identifier |> SyntaxFactory.IdentifierName |> SyntaxFactory.Argument ]
                |> SF.variableDeclaration "dataBytes"
                |> SyntaxFactory.LocalDeclarationStatement
                :> StatementSyntax

                SyntaxFactory
                    .UsingStatement(
                        SyntaxFactory.IdentifierName parseMethodName
                        |> SF.methodInvocation [ SyntaxFactory.IdentifierName "dataStream" |> SyntaxFactory.Argument ]
                        |> SyntaxFactory.ReturnStatement
                    )
                    .WithDeclaration(
                        SyntaxFactory.ParseTypeName "System.IO.MemoryStream"
                        |> SF.objectCreation [ SyntaxFactory.IdentifierName "dataBytes" |> SyntaxFactory.Argument ]
                        |> SF.variableDeclaration "dataStream"
                    )
                :> StatementSyntax
            ]
            |> SyntaxFactory.Block
        )

let getCreationMethods (rootTypeSyntax: TypeSyntax) sampleData (parseStreamStatements: TypeSyntax -> ParameterSyntax -> StatementSyntax list) mimeType =
    let parseStreamMethod =
        let parseStreamParam =
            SyntaxFactory.ParseTypeName "System.IO.Stream"
            |> SF.parameter "dataStream"

        SyntaxFactory
            .MethodDeclaration(rootTypeSyntax, SyntaxFactory.Identifier parseMethodName)
            .WithModifiers(
                [
                    SyntaxKind.PublicKeyword
                    SyntaxKind.StaticKeyword
                ]
                |> SF.getKeywordTokenList
            )
            .WithParameterList(SF.singletonParameter parseStreamParam)
            .WithBody(
                parseStreamStatements rootTypeSyntax parseStreamParam
                |> SyntaxFactory.Block
            )
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
        [
            SyntaxFactory.IdentifierName "System" :> SimpleNameSyntax
            SyntaxFactory.IdentifierName "Collections" :> SimpleNameSyntax
            SyntaxFactory.IdentifierName "Generic" :> SimpleNameSyntax
            SF.genericName "IReadOnlyList" [ getTypeSyntax s ] :> SimpleNameSyntax
        ]
        |> SF.qualifiedTypeName
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
                    SyntaxFactory
                        .AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(
                            SyntaxKind.SemicolonToken
                            |> SyntaxFactory.Token
                        )
                    |> SyntaxFactory.SingletonList
                    |> SyntaxFactory.AccessorList
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
