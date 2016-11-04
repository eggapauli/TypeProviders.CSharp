module TypeProviders.CSharp.Test.Parse

open Swensen.Unquote
open Xunit
open TypeProviders.CSharp

let propertyTypeTestData: obj array array = [|
    [| "\"asdqwe\""; Predefined TString |]
    [| "5"; Predefined TInt |]
    [| "5.123"; Predefined TDouble |]
    [| "true"; Predefined TBool |]
    [| "\"2009-06-15T13:45:30Z\""; Common "System.DateTime" |]
    [| "\"7E22EDE9-6D0F-48C2-A280-B36DC859435D\""; Common "System.Guid" |]
    [| "\"05:04:03\""; Common "System.TimeSpan" |]
    [| "\"http://example.com/path?query#hash\""; Common "System.Uri" |]
|]

[<Theory>]
[<MemberData("propertyTypeTestData")>]
let ``Should parse correct property type``(value, expectedType) =
    let actual =
        sprintf """{ "Value": %s }""" value
        |> JsonProviderArgs.create
        |> JsonProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Common "Root"
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Value", expectedType)
                    ]
                )
        ]
    }
    actual =! expected

[<Fact>]
let ``Should work with multiple object properties``() =
    let actual =
        JsonProviderArgs.create """{ "A": 5, "B": "test" }"""
        |> JsonProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Common "Root"
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("A", Predefined TInt)
                        Property ("B", Predefined TString)
                    ]
                )
        ]
    }
    actual =! expected

[<Fact>]
let ``Should refactor according to sample data with nested object``() =
    let actual =
        JsonProviderArgs.create """{ "Obj": { "Value": 5 } }"""
        |> JsonProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Common "Root"
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Obj", Common "Obj")
                    ]
                )
            SubType
                (
                    "Obj",
                    [
                        Property ("Value", Predefined TInt)
                    ]
                )
        ]
    }
    actual =! expected

[<Fact>]
let ``Should refactor according to sample data with simple array``() =
    let actual =
        JsonProviderArgs.create """{ "Values": [ 1, 2, 3 ] }"""
        |> JsonProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Common "Root"
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Values", Predefined TInt |> Collection)
                    ]
                )
        ]
    }
    actual =! expected

[<Fact>]
let ``Should refactor according to sample data with array of objects``() =
    let actual =
        JsonProviderArgs.create """{ "Values": [ { "Value": 1 }, { "Value": 2 }, { "Value": 3 } ] }"""
        |> JsonProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Common "Root"
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Values", Common "Value" |> Collection)
                    ]
                )
            SubType
                (
                    "Value",
                    [
                        Property ("Value", Predefined TInt)
                    ]
                )
        ]
    }
    actual =! expected

[<Fact>]
let ``Should refactor according to sample data with array of simple array``() =
    let actual =
        JsonProviderArgs.create """{ "Values": [ [ 1, 2 ], [ 3, 4 ] ] }"""
        |> JsonProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Common "Root"
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Values", Predefined TInt |> Collection |> Collection)
                    ]
                )
        ]
    }
    actual =! expected

[<Fact>]
let ``Should refactor according to sample data with array of array of object``() =
    let actual =
        JsonProviderArgs.create """{ "Values": [ [ { "Value": 1 }, { "Value": 2 } ], [ { "Value": 3 }, { "Value": 4 } ] ] }"""
        |> JsonProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Common "Root"
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Values", Common "Value" |> Collection |> Collection)
                    ]
                )
            SubType
                (
                    "Value",
                    [
                        Property ("Value", Predefined TInt)
                    ]
                )
        ]
    }
    actual =! expected

[<Fact>]
let ``Should parse simple array type``() =
    let actual =
        JsonProviderArgs.create """[ 1, 2, 3 ]"""
        |> JsonProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Predefined TInt |> Collection
        Members = []
    }
    actual =! expected

[<Fact>]
let ``Should refactor according to object array``() =
    let actual =
        JsonProviderArgs.create """[{ "Value": 1 }, { "Value": 2 }, { "Value": 3 }]"""
        |> JsonProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Common "Root" |> Collection
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Value", Predefined TInt)
                    ]
                )
        ]
    }
    actual =! expected

[<Fact>]
let ``Should create combined type for all array elements``() =
    let actual =
        JsonProviderArgs.create """[ { "a": 5 }, { "b": "text" } ]"""
        |> JsonProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Common "Root" |> Collection
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("A", Predefined TInt |> Optional)
                        Property ("B", Predefined TString)
                    ]
                )
        ]
    }
    actual =! expected
