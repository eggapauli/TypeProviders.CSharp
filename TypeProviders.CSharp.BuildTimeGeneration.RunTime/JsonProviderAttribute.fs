namespace TypeProviders.CSharp.BuildTimeGeneration

open System
open System.Diagnostics
open CodeGeneration.Roslyn

[<AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)>]
[<CodeGenerationAttribute("TypeProviders.CSharp.BuildTimeGeneration.JsonProviderGenerator, TypeProviders.CSharp.BuildTimeGeneration, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")>]
[<Conditional("CodeGeneration")>]
type JsonProviderAttribute(data: string) =
    inherit Attribute()
