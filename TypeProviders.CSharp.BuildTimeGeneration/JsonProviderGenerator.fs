namespace TypeProviders.CSharp.BuildTimeGeneration

open System
open System.Threading
open System.Threading.Tasks
open CodeGeneration.Roslyn
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open TypeProviders.CSharp

type DiagnosticsType =
    | Debug of string
    | GeneralError of string * string

[<AutoOpen>]
module Diagnostics_ =
    module Diagnostics =
        let toDescriptor = function
            | Debug message ->
                DiagnosticDescriptor(
                    "TPCS001",
                    "Debug message",
                    message,
                    "",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault = true,
                    description = null,
                    helpLinkUri = null
                )
            | GeneralError (title, message) ->
                DiagnosticDescriptor(
                    "TPCS002",
                    title,
                    message,
                    "",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault = true,
                    description = null,
                    helpLinkUri = null
                )
        let create location diagnosticsType = 
            Diagnostic.Create(toDescriptor diagnosticsType, location)

type JsonProviderGenerator(attributeData: AttributeData) =
    let sampleData = attributeData.ConstructorArguments.[0].Value :?> string

    interface ICodeGenerator with
        member x.GenerateAsync(applyTo: MemberDeclarationSyntax, document: Document, progress: IProgress<Diagnostic>, ct) =
            let parseSampleData =
                JsonProviderArgs.create
                >> JsonProviderBridge.parseDataType

            applyTo
            :?> ClassDeclarationSyntax
            |> Option.ofObj
            |> Option.map (fun typeDecl ->
                let members =
                    try
                        let dataType = 
                            parseSampleData sampleData
                            |> DataTypeUpdate.CSharp.ensureTypeHasNoPropertyWithSameName
                    
                        [
                            yield! CodeGeneration.generateDataStructure dataType
                            yield! CodeGeneration.generateCreationMethods dataType sampleData
                        ]
                    with e ->
                        GeneralError ("Error while generating code", e.Message)
                        |> Diagnostics.create (applyTo.GetLocation())
                        |> progress.Report

                        reraise()

                        []

                let partialClass =
                    SyntaxFactory
                        .ClassDeclaration(typeDecl.Identifier)
                        .AddModifiers(SyntaxFactory.Token SyntaxKind.PartialKeyword)
                        .WithMembers(SyntaxFactory.List members)

                partialClass
                :> MemberDeclarationSyntax
                |> List.singleton
            )
            |> Option.ifNone []
            |> SyntaxFactory.List
            |> Task.FromResult
