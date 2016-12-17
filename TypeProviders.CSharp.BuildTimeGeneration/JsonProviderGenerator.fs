namespace TypeProviders.CSharp.BuildTimeGeneration

open System
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp.Syntax
open CodeGeneration.Roslyn
open TypeProviders.CSharp

type JsonProviderGenerator(attributeData: AttributeData) =
    let sampleData = attributeData.ConstructorArguments.[0].Value :?> string

    interface ICodeGenerator with
        member x.GenerateAsync(applyTo: MemberDeclarationSyntax, document: Document, progress: IProgress<Diagnostic>, ct) =
            let generateMembers() =
                let dataType = 
                    JsonProviderArgs.create sampleData
                    |> JsonProviderBridge.parseDataType
                    |> DataTypeUpdate.CSharp.ensureTypeHasNoPropertyWithSameName
                   
                [
                    yield! JsonCodeGeneration.generateDataStructure dataType
                    yield! JsonCodeGeneration.generateCreationMethods dataType sampleData
                ]

            CodeGenerationBridge.generate applyTo progress generateMembers
