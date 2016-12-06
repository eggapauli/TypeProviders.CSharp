module TypeProviders.CSharp.Test.DataTypeUpdate

open Swensen.Unquote
open Xunit
open TypeProviders.CSharp

[<Fact>]
let ``Should not change anything when names dont collide``() =
    let input = {
        ReturnTypeFromParsingData = Generated "Root" |> Collection
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Values", Generated "Value" |> Collection)
                        Property ("Time", Existing "System.DateTime")
                    ]
                )
            SubType
                (
                    "Value",
                    [
                        Property ("Val", Predefined TInt)
                    ]
                )
        ]
    }

    DataTypeUpdate.CSharp.ensureTypeHasNoPropertyWithSameName input =! input

[<Fact>]
let ``Should change type name when names collide``() =
    let actual =
        {
            ReturnTypeFromParsingData = Generated "Root" |> Collection
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
                            Property ("Value", Predefined TInt)
                        ]
                    )
            ]
        }
        |> DataTypeUpdate.CSharp.ensureTypeHasNoPropertyWithSameName

    let expected = {
        ReturnTypeFromParsingData = Generated "Root" |> Collection
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Values", Generated "Value_" |> Collection)
                    ]
                )
            SubType
                (
                    "Value_",
                    [
                        Property ("Value", Predefined TInt)
                    ]
                )
        ]
    }

    actual =! expected
