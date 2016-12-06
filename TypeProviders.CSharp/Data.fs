namespace TypeProviders.CSharp

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp.Syntax
open System.Linq
open Microsoft.CodeAnalysis.CSharp

type PredefinedCSharpType =
    | TBool
    | TByte
    | TSByte
    | TChar
    | TDecimal
    | TDouble
    | TFloat
    | TInt
    | TUInt
    | TLong
    | TULong
    | TObject
    | TShort
    | TUShort
    | TString

type TypeName =
    | Generated of string
    | Existing of string
    | Collection of TypeName
    | Predefined of PredefinedCSharpType
    | Optional of TypeName

type DataTypeMember =
    | SubType of string * DataTypeMember list
    | Property of string * TypeName

type DataType = {
    ReturnTypeFromParsingData: TypeName
    Members: DataTypeMember list
}
