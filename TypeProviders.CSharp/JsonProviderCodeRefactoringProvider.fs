namespace TypeProviders.CSharp

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CodeActions
open Microsoft.CodeAnalysis.CodeRefactorings
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Microsoft.CodeAnalysis.Formatting
open System.Composition
open System
open System.Threading
open ProviderImplementation
open FSharp.Data.Runtime
open FSharp.Data.Runtime.StructuralTypes
open FSharp.Data
open FSharp.Data.Runtime.BaseTypes
open Microsoft.FSharp.Core.CompilerServices
open System.Reflection
open System.IO
open ProviderImplementation.ProvidedTypes

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

    let updateTypeProviderAsync (document: Document) (typeDecl: ClassDeclarationSyntax) (sampleData: string) (ct: CancellationToken) =
        async {
            let! syntaxRoot = document.GetSyntaxRootAsync ct |> Async.awaitTaskAllowContextSwitch

            try
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

    override x.ComputeRefactoringsAsync (context: CodeRefactoringContext) =
        async {
            let! root =
                context
                    .Document
                    .GetSyntaxRootAsync(context.CancellationToken)
                |> Async.awaitTaskAllowContextSwitch

            let node = root.FindNode context.Span

            return!
                node :?> ClassDeclarationSyntax
                |> Option.ofObj
                |> Option.map (fun typeDecl -> async {
                    let! semanticModel =
                        context
                            .Document
                            .GetSemanticModelAsync(context.CancellationToken)
                        |> Async.awaitTaskAllowContextSwitch

                    tryGetTypeProviderSampleData typeDecl attributeFullName semanticModel
                    |> Option.iter (fun sampleData ->
                        CodeAction.Create
                            (
                                "Synchronize type provider with sample data",
                                Func<_, _>(updateTypeProviderAsync context.Document typeDecl sampleData >> Async.StartAsTask)
                            )
                        |> context.RegisterRefactoring
                    )
                })
                |> Option.ifNone (async { return () })
        }
        |> Async.StartAsTask
        :> System.Threading.Tasks.Task
