namespace TypeProviders.CSharp.CodeRefactoringProvider

open System.Composition
open Microsoft.CodeAnalysis.CodeRefactorings
open Microsoft.CodeAnalysis
open TypeProviders.CSharp

[<ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = "JsonProviderCodeRefactoringProvider")>]
[<Shared>]
type JsonProviderCodeRefactoringProvider() =
    inherit CodeRefactoringProvider()

    override x.ComputeRefactoringsAsync (context: CodeRefactoringContext) =
        async {
            let generateMembers sampleData =
                let dataType = 
                    JsonProviderArgs.create sampleData
                    |> JsonProviderBridge.parseDataType
                    |> DataTypeUpdate.CSharp.ensureTypeHasNoPropertyWithSameName

                [
                    yield! JsonCodeGeneration.generateDataStructure dataType
                    yield! JsonCodeGeneration.generateCreationMethods dataType sampleData
                ]

            let attributeFullName = typeof<JsonProviderAttribute>.FullName

            let! refactorings = CodeRefactoringProviderHelper.getRefactorings context attributeFullName generateMembers context.CancellationToken

            refactorings
            |> List.iter context.RegisterRefactoring
        }
        |> Async.StartAsTask
        :> System.Threading.Tasks.Task
