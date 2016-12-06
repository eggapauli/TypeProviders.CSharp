module TypeProviders.CSharp.Test.XmlProviderParser

open Swensen.Unquote
open Xunit
open TypeProviders.CSharp

let propertyTypeTestData: obj array array = [|
    [| "\"asdqwe\""; Predefined TString |]
    [| "5"; Predefined TInt |]
    //[| "5.123"; Predefined TDouble |] // FSharp.Data parses it as decimal
    [| "5.123"; Predefined TDecimal |]
    [| "true"; Predefined TBool |]
    //[| "\"2009-06-15T13:45:30Z\""; Common "System.DateTime" |] // No support in FSharp.Data
    //[| "\"7E22EDE9-6D0F-48C2-A280-B36DC859435D\""; Common "System.Guid" |] // No support in FSharp.Data
    //[| "\"05:04:03\""; Common "System.TimeSpan" |] // No support in FSharp.Data
    //[| "\"http://example.com/path?query#hash\""; Common "System.Uri" |] // No support in FSharp.Data
|]

[<Theory>]
[<MemberData("propertyTypeTestData")>]
let ``Should parse correct property type``(value, expectedType) =
    let actual =
        sprintf "<Value>%s</Value>" value
        |> XmlProviderArgs.create
        |> XmlProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = expectedType
        Members = [ ]
    }
    actual =! expected

[<Fact>]
let ``Should work with multiple object properties``() =
    let actual =
        XmlProviderArgs.create """<Root A="5" B="test" />"""
        |> XmlProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Generated "Root"
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
        XmlProviderArgs.create """<Root><Obj Value="5" /></Root>"""
        |> XmlProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Generated "Root"
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Obj", Generated "Obj")
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
        XmlProviderArgs.create "<Root><Value>1</Value><Value>2</Value><Value>3</Value></Root>"
        |> XmlProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Generated "Root"
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
        XmlProviderArgs.create "<Root><Values><Value><Number>1</Number></Value><Value><Number>2</Number></Value><Value><Number>3</Number></Value></Values></Root>"
        |> XmlProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Generated "Root"
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Values", Generated "Value" |> Collection)
                    ]
                )
            SubType
                (
                    "Value",
                    [
                        Property ("Number", Predefined TInt)
                    ]
                )
        ]
    }
    actual =! expected

[<Fact>]
let ``Should create combined type for all array elements``() =
    let actual =
        XmlProviderArgs.create """<Root><Item A="5" /><Item B="text" /></Root>"""
        |> XmlProviderBridge.parseDataType

    let expected = {
        ReturnTypeFromParsingData = Generated "Root"
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Items", Generated "Item" |> Collection)
                    ]
                )
            SubType
                (
                    "Item",
                    [
                        Property ("A", Predefined TInt |> Optional)
                        Property ("B", Predefined TString)
                    ]
                )
        ]
    }
    actual =! expected
