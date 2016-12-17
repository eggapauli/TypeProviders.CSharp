module TypeProviders.CSharp.CodeRefactoringProvider.CodeRefactoringProviderHelper

open System
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Microsoft.CodeAnalysis.Formatting
open Microsoft.CodeAnalysis.CodeActions
open Microsoft.CodeAnalysis.CodeRefactorings
open TypeProviders.CSharp

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

let updateTypeProviderAsync (document: Document) (typeDecl: ClassDeclarationSyntax) sampleData generateMembers ct =
    async {
        let! syntaxRoot = document.GetSyntaxRootAsync ct |> Async.awaitTaskAllowContextSwitch

        try
            let members = generateMembers sampleData

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

let createCodeAction document typeDecl generateMembers sampleData =
    CodeAction.Create(
        "Generate members from sample data",
        Func<_, _>(updateTypeProviderAsync document typeDecl sampleData generateMembers >> Async.StartAsTask)
    )

let findContextNode (context: CodeRefactoringContext) = async {
    let! root =
        context
            .Document
            .GetSyntaxRootAsync(context.CancellationToken)
        |> Async.awaitTaskAllowContextSwitch

    return root.FindNode context.Span
}

let getRefactorings (context: CodeRefactoringContext) attributeFullName generateMembers ct = async {
    let document = context.Document
    let! node = findContextNode context
    match node with
    | :? ClassDeclarationSyntax as typeDecl ->
        let! semanticModel = getSemanticModel document ct

        return
            tryGetTypeProviderSampleData typeDecl attributeFullName semanticModel
            |> Option.map (createCodeAction document typeDecl generateMembers)
            |> Option.toList
    | _ ->  return []
}
