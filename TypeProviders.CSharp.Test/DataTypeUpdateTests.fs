module TypeProviders.CSharp.Test.DataTypeUpdateTests

open Swensen.Unquote
open Xunit
open TypeProviders.CSharp

[<Fact>]
let ``Should not change anything when names dont collide``() =
    let input = {
        ReturnTypeFromParsingData = Common "Root" |> Collection
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Values", Common "Value" |> Collection)
                        Property ("Time", Common "System.DateTime")
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
            ReturnTypeFromParsingData = Common "Root" |> Collection
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
        |> DataTypeUpdate.CSharp.ensureTypeHasNoPropertyWithSameName

    let expected = {
        ReturnTypeFromParsingData = Common "Root" |> Collection
        Members =
        [
            SubType
                (
                    "Root",
                    [
                        Property ("Values", Common "Value_" |> Collection)
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
