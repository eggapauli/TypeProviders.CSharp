module TypeProviders.CSharp.BuildTimeGeneration.Test.ParseTest

open System
open Swensen.Unquote
open Xunit

[<Fact>]
let ``Parsing should work``() =
    let data = SimpleJsonProvider.Parse """{"a": 5, "b": "test"}"""
    data.A =! 5
    data.B =! "test"

[<Fact>]
let ``Loading from file should work``() =
    let data = SimpleJsonProvider.Load ".\\SampleData.json"
    data.A =! 5
    data.B =! "test"

[<Fact>]
let ``Loading from uri should work``() =
    use d = SampleWebServer.setup """{"a": 5, "b": "test"}"""
    let data =
        "http://localhost:6666/"
        |> Uri
        |> SimpleJsonProvider.LoadAsync
        |> Async.AwaitTask
        |> Async.RunSynchronously
    data.A =! 5
    data.B =! "test"
