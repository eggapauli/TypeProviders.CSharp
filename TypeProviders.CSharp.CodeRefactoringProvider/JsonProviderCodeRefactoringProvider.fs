namespace TypeProviders.CSharp

open System
open System.Composition
open System.Threading
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CodeActions
open Microsoft.CodeAnalysis.CodeRefactorings
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Microsoft.CodeAnalysis.Formatting
open ProviderImplementation

[<ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = "JsonProviderCodeRefactoringProvider")>]
[<Shared>]
type JsonProviderCodeRefactoringProvider() =
    inherit CodeRefactoringProvider()

    let attributeFullName = typeof<Providers.JsonProviderAttribute>.FullName

    let tryGetTypeProviderSampleData (typeDecl: SyntaxNode) typeProviderAttributeName (semanticModel: SemanticModel) =
        semanticModel
            .Compilation
            .GetTypeByMetadataName typeProviderAttributeName
        |> Option.ofObj
        |> Option.bind (fun attributeSymbol ->
            let typeSymbol = semanticModel.GetDeclaredSymbol typeDecl
            
            typeSymbol.GetAttributes()
            |> Seq.tryFind(fun attr -> attr.AttributeClass.Equals attributeSymbol)
        )
        |> Option.bind(fun attribute -> attribute.ConstructorArguments |> Seq.tryHead)
        |> Option.bind(fun sampleSourceArgument ->
            sampleSourceArgument.Value
            |> Option.cast<string>
        )

    let generateCode dataType (document: Document) (typeDecl: ClassDeclarationSyntax) sampleData (ct: CancellationToken) =
        async {
            let! syntaxRoot = document.GetSyntaxRootAsync ct |> Async.awaitTaskAllowContextSwitch

            try
                let members = [
                    yield! CodeGeneration.generateDataStructure dataType
                    yield! CodeGeneration.generateCreationMethods dataType sampleData
                ]

                let newTypeDecl =
                    typeDecl
                        .WithMembers(SyntaxFactory.List members)
                        .WithAdditionalAnnotations(Formatter.Annotation)
                return
                    syntaxRoot.ReplaceNode(typeDecl, newTypeDecl)
                    |> document.WithSyntaxRoot
            with e ->
                let newTypeDecl =
                    typeDecl
                        .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>())
                        .WithOpenBraceToken(
                            SyntaxFactory
                                .Token(SyntaxKind.OpenBraceToken)
                                .WithTrailingTrivia(
                                    SyntaxFactory.CarriageReturnLineFeed,
                                    SyntaxFactory.Comment <| sprintf "/* %s */" e.Message,
                                    SyntaxFactory.CarriageReturnLineFeed
                                )
                            );
                return
                    syntaxRoot.ReplaceNode(typeDecl, newTypeDecl)
                    |> document.WithSyntaxRoot
        }

    let updateTypeProviderAsync document typeDecl sampleData ct =
        let args: JsonProviderArgs = {
            Sample = sampleData
            SampleIsList = false
            RootName = "Root"
            Culture = ""
            Encoding = ""
            ResolutionFolder = ""
            EmbeddedResource = ""
            InferTypesFromValues = true
        }
        let dataType =
            JsonProviderBridge.parseDataType args
            |> DataTypeUpdate.CSharp.ensureTypeHasNoPropertyWithSameName

        generateCode dataType document typeDecl sampleData ct

    let getSemanticModel (document: Document) ct =
        document.GetSemanticModelAsync ct
        |> Async.awaitTaskAllowContextSwitch

    let createCodeAction document typeDecl sampleData =
        CodeAction.Create(
            "Generate members from sample data",
            Func<_, _>(updateTypeProviderAsync document typeDecl sampleData >> Async.StartAsTask)
        )

    let getRefactorings document typeDecl attributeFullName ct = async {
        let! semanticModel = getSemanticModel document ct

        return
            tryGetTypeProviderSampleData typeDecl attributeFullName semanticModel
            |> Option.map (createCodeAction document typeDecl)
            |> Option.toList
    }

    let findContextNode (context: CodeRefactoringContext) = async {
        let! root =
            context
                .Document
                .GetSyntaxRootAsync(context.CancellationToken)
            |> Async.awaitTaskAllowContextSwitch

        return root.FindNode context.Span
    }

    override x.ComputeRefactoringsAsync (context: CodeRefactoringContext) =
        async {
            try
                let! node = findContextNode context

                let! refactorings =
                    match node with
                    | :? ClassDeclarationSyntax as typeDecl ->
                        getRefactorings context.Document typeDecl attributeFullName context.CancellationToken
                    | _ ->  async { return [] }

                refactorings
                |> List.iter context.RegisterRefactoring
            with e ->
                printfn "%O" e
        }
        |> Async.StartAsTask
        :> System.Threading.Tasks.Task
