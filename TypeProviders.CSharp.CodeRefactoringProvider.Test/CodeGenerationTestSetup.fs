module TypeProviders.CSharp.Test.CodeGeneration.TestSetup

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CodeActions
open Microsoft.CodeAnalysis.CodeRefactorings
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.Text
open System.Collections.Generic
open System.Linq
open System.Reflection
open System.Threading
open System.Threading.Tasks
open TypeProviders.CSharp
open Xunit
open System
open Swensen.Unquote
open System.Collections.Immutable

let indentLines indentationLevel lines =
    let spacesPerIndentLevel = 4
    let indentSpaces = indentationLevel * spacesPerIndentLevel
    lines
    |> List.map (fun l ->
        if l = ""
        then l
        else (String.replicate indentSpaces " ") + l
    )
    |> String.concat Environment.NewLine

let metaDataReferenceFromType<'a> =
    typeof<'a>.Assembly.Location
    |> MetadataReference.CreateFromFile

let createDocument metadataReferences (code: string) =
    let fileNamePrefix = "Test"
    let fileExt = "cs"
    let projectName = "TestProject"

    let corlibReference = metaDataReferenceFromType<obj>
    let systemReference = metaDataReferenceFromType<Uri>
    let systemCoreReference = metaDataReferenceFromType<Enumerable>
    let cSharpSymbolsReference = metaDataReferenceFromType<CSharpCompilation>
    let codeAnalysisReference = metaDataReferenceFromType<Compilation>
    let netHttpReference = MetadataReference.CreateFromFile(Assembly.Load("System.Net.Http, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location)
    let typeProvidersAssembly = metaDataReferenceFromType<TypeProviders.CSharp.JsonProviderAttribute>
    let systemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location)

    let projectId = ProjectId.CreateNewId(debugName = projectName)
    let compilationOptions = CSharpCompilationOptions OutputKind.DynamicallyLinkedLibrary

    let newFileName = sprintf "%s.%s" fileNamePrefix fileExt
    let documentId = DocumentId.CreateNewId(projectId, debugName = newFileName)

    let workspace = new AdhocWorkspace()
    let solution =
        workspace
            .CurrentSolution
            .AddProject(projectId, projectName, projectName, LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, compilationOptions)
            .AddMetadataReference(projectId, corlibReference)
            .AddMetadataReference(projectId, systemReference)
            .AddMetadataReference(projectId, systemCoreReference)
            .AddMetadataReference(projectId, cSharpSymbolsReference)
            .AddMetadataReference(projectId, codeAnalysisReference)
            .AddMetadataReference(projectId, netHttpReference)
            .AddMetadataReference(projectId, typeProvidersAssembly)
            .AddMetadataReference(projectId, systemRuntimeReference)
            .AddMetadataReferences(projectId, metadataReferences)
            .AddDocument(documentId, newFileName, code)

    solution.GetDocument documentId

let getRefactoring (codeRefactoringProvider: CodeRefactoringProvider) (document: Document) =
    let root = document.GetSyntaxRootAsync().Result
    let refactorings = System.Collections.Generic.List<_>()
    let context = new CodeRefactoringContext(document, root.Span, Action<_>(refactorings.Add), CancellationToken.None);
    codeRefactoringProvider.ComputeRefactoringsAsync(context).Wait()
    refactorings
    |> Seq.tryHead

let getAndApplyRefactoring codeRefactoringProvider document =
    match getRefactoring codeRefactoringProvider document with
    | Some action ->
        let operations = action.GetOperationsAsync(CancellationToken.None).Result
        let solution =
            operations
            |> Seq.ofType<ApplyChangesOperation>
            |> Seq.exactlyOne
            |> fun o -> o.ChangedSolution
        let newDocument = solution.GetDocument(document.Id)
        let syntaxRoot = newDocument.GetSyntaxRootAsync().Result
        let compilation = newDocument.Project.GetCompilationAsync().Result
        compilation.GetDiagnostics() =! ImmutableArray<Diagnostic>.Empty
        newDocument
    | None -> failwith "Can't apply <null> refactoring"
