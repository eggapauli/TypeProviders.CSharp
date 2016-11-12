namespace TypeProviders.CSharp.BuildTimeGeneration

open System
open System.Diagnostics
open CodeGeneration.Roslyn

[<AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)>]
[<CodeGenerationAttribute(typeof<JsonProviderGenerator>)>]
[<Conditional("CodeGeneration")>]
type JsonProviderAttribute(data: string) =
    inherit Attribute()
