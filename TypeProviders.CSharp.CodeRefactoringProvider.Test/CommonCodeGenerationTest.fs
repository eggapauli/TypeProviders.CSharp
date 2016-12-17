module TypeProviders.CSharp.Test.CodeGeneration.Common

open TypeProviders.CSharp.CodeRefactoringProvider
open Xunit
open Swensen.Unquote

let private createCodeRefactoringProvider() =
    JsonProviderCodeRefactoringProvider()

let private createDocument code =
    let jsonNetReference = TestSetup.metaDataReferenceFromType<Newtonsoft.Json.JsonConvert>

    TestSetup.createDocument [ jsonNetReference ] code

let private getRefactoring =
    TestSetup.getRefactoring (createCodeRefactoringProvider())

[<Fact>]
let ``Should not have refactoring when attribute not set``() =
    "class TestProvider { }"
    |> createDocument
    |> getRefactoring
    |> Option.isSome =! false

[<Fact>]
let ``Should not have refactoring when sample data argument is missing``() =
    """
[TypeProviders.CSharp.JsonProvider]
class TestProvider
{
}
"""
    |> createDocument
    |> getRefactoring
    |> Option.isSome =! false

[<Fact>]
let ``Should not have refactoring when sample data argument has wrong type``() =
    """
[TypeProviders.CSharp.JsonProvider(5)]
class TestProvider
{
}
"""
    |> createDocument
    |> getRefactoring
    |> Option.isSome =! false

[<Fact>]
let ``Should have refactoring for simple sample data``() =
    """
[TypeProviders.CSharp.JsonProvider("{ \"asd\": \"qwe\" }")]
class TestProvider
{
}
"""
    |> createDocument
    |> getRefactoring
    |> Option.isSome =! true
