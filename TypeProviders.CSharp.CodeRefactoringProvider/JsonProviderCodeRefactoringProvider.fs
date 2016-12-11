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

    let attributeFullName = typeof<JsonProviderAttribute>.FullName

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

    let updateTypeProviderAsync (document: Document) (typeDecl: ClassDeclarationSyntax) sampleData parseSampleData ct =
        async {
            let! syntaxRoot = document.GetSyntaxRootAsync ct |> Async.awaitTaskAllowContextSwitch

            try
                let dataType = 
                    parseSampleData sampleData
                    |> DataTypeUpdate.CSharp.ensureTypeHasNoPropertyWithSameName

                let members = [
                    yield! JsonCodeGeneration.generateDataStructure dataType
                    yield! JsonCodeGeneration.generateCreationMethods dataType sampleData
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

    let getSemanticModel (document: Document) ct =
        document.GetSemanticModelAsync ct
        |> Async.awaitTaskAllowContextSwitch

    let createCodeAction document typeDecl parseSampleData sampleData =
        CodeAction.Create(
            "Generate members from sample data",
            Func<_, _>(updateTypeProviderAsync document typeDecl sampleData parseSampleData >> Async.StartAsTask)
        )

    let getRefactorings document typeDecl attributeFullName parseSampleData ct = async {
        let! semanticModel = getSemanticModel document ct

        return
            tryGetTypeProviderSampleData typeDecl attributeFullName semanticModel
            |> Option.map (createCodeAction document typeDecl parseSampleData)
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
            let! node = findContextNode context

            let parseSampleData =
                JsonProviderArgs.create
                >> JsonProviderBridge.parseDataType

            let! refactorings =
                match node with
                | :? ClassDeclarationSyntax as typeDecl ->
                    getRefactorings context.Document typeDecl attributeFullName parseSampleData context.CancellationToken
                | _ ->  async { return [] }

            refactorings
            |> List.iter context.RegisterRefactoring
        }
        |> Async.StartAsTask
        :> System.Threading.Tasks.Task
