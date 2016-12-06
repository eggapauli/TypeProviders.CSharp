namespace TypeProviders.CSharp.CodeRefactoringProvider

open System.Composition
open Microsoft.CodeAnalysis.CodeRefactorings
open Microsoft.CodeAnalysis
open TypeProviders.CSharp

[<ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = "XmlProviderCodeRefactoringProvider")>]
[<Shared>]
type XmlProviderCodeRefactoringProvider() =
    inherit CodeRefactoringProvider()

    override x.ComputeRefactoringsAsync (context: CodeRefactoringContext) =
        async {
            let generateMembers sampleData =
                let dataType = 
                    XmlProviderArgs.create sampleData
                    |> XmlProviderBridge.parseDataType
                    |> DataTypeUpdate.CSharp.ensureTypeHasNoPropertyWithSameName

                [
                    yield! XmlCodeGeneration.generateDataStructure dataType
                    yield! XmlCodeGeneration.generateCreationMethods dataType sampleData
                ]

            let attributeFullName = typeof<XmlProviderAttribute>.FullName

            let! refactorings = CodeRefactoringProviderHelper.getRefactorings context attributeFullName generateMembers context.CancellationToken

            refactorings
            |> List.iter context.RegisterRefactoring
        }
        |> Async.StartAsTask
        :> System.Threading.Tasks.Task
