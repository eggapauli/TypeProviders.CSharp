[<AutoOpen>]
module TypeProviders.CSharp.TypeName_

open System
open System.Reflection
open TypeProviders.CSharp
open FSharp.Data
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax

module TypeName =
    let rec fromType (t: Type) =
        if t.IsArray
        then
            t.GetElementType()
            |> fromType
            |> Collection
        else
            let (=!) (t1: Type) (t2: Type) =
                t1.Assembly.FullName = t2.Assembly.FullName
                && t1.FullName = t2.FullName

            let isOptional (ty: Type) =
                ty.IsGenericType &&
                    (ty.GetGenericTypeDefinition() =! typedefof<option<_>>
                    || ty.GetGenericTypeDefinition() =! typedefof<Nullable<_>>)

            if t =! typeof<bool> then Predefined TBool
            elif t =! typeof<byte> then Predefined TByte
            elif t =! typeof<sbyte> then Predefined TSByte
            elif t =! typeof<char> then Predefined TChar
            elif t =! typeof<decimal> then Predefined TDecimal
            elif t =! typeof<double> then Predefined TDouble
            elif t =! typeof<float> then Predefined TFloat
            elif t =! typeof<int> then Predefined TInt
            elif t =! typeof<uint32> then Predefined TUInt
            elif t =! typeof<int64> then Predefined TLong
            elif t =! typeof<uint64> then Predefined TULong
            elif t =! typeof<obj> then Predefined TObject
            elif t =! typeof<int16> then Predefined TShort
            elif t =! typeof<uint16> then Predefined TUShort
            elif t =! typeof<string> then Predefined TString
            elif isOptional t
            then
                let elementType = t.GetGenericArguments().[0]
                let elementTypeName = fromType elementType
                if elementType.IsValueType
                then Optional elementTypeName
                else elementTypeName
            elif t.IsNested then Common t.Name // TODO works only for our types, not for existing ones
            elif t.FullName = typeof<FSharp.Data.Runtime.BaseTypes.IJsonDocument>.FullName
            then Predefined TObject
            else Common t.FullName
