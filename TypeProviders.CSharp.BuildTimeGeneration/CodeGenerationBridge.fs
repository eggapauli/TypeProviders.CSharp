namespace TypeProviders.CSharp.BuildTimeGeneration

open System
open System.Threading.Tasks
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open TypeProviders.CSharp

type DiagnosticsType =
    | Debug of string
    | GeneralError of string * string

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

module CodeGenerationBridge =
    let generate (applyTo: MemberDeclarationSyntax) (progress: IProgress<Diagnostic>) fn =
        applyTo
        :?> ClassDeclarationSyntax
        |> Option.ofObj
        |> Option.map (fun typeDecl ->
            let members =
                try
                    fn()
                with e ->
                    GeneralError ("Error while generating code", e.Message)
                    |> Diagnostics.create (applyTo.GetLocation())
                    |> progress.Report

                    reraise(); []

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
