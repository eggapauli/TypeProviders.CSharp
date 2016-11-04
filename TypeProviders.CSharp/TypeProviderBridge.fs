module TypeProviders.CSharp.TypeProviderBridge

open System
open System.Reflection
open TypeProviders.CSharp
open FSharp.Data
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax

let getMembers (ty: Type) =
    let isGetterOrSetter m =
        ty.GetProperties()
        |> Seq.exists(fun p -> p.GetGetMethod() = m || p.GetSetMethod() = m)
    [
        yield! ty.GetProperties() |> Seq.cast<MemberInfo>
        //yield! ty.GetMethods() |> Seq.cast<MemberInfo>
        yield! ty.GetNestedTypes() |> Seq.cast<MemberInfo>
    ]
    |> List.filter (function
        | :? PropertyInfo as p when p.PropertyType.FullName = typeof<JsonValue>.FullName -> false
        | :? MethodInfo as m when isGetterOrSetter m -> false
        | _ -> true
    )

let getProperties (ty: Type) =
    getMembers ty
    |> Seq.ofType<PropertyInfo>
    |> Seq.toList

let rec getTypeName (t: Type) =
    if t.IsArray
    then
        t.GetElementType()
        |> getTypeName
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
            let elementTypeName = getTypeName elementType
            if elementType.IsValueType
            then Optional elementTypeName
            else elementTypeName
        elif t.IsNested then Common t.Name // TODO works only for our types, not for existing ones
        else Common t.FullName

let toSyntaxKind = function
    | TBool -> SyntaxKind.BoolKeyword
    | TByte -> SyntaxKind.ByteKeyword
    | TSByte -> SyntaxKind.SByteKeyword
    | TChar -> SyntaxKind.CharKeyword
    | TDecimal -> SyntaxKind.DecimalKeyword
    | TDouble -> SyntaxKind.DoubleKeyword
    | TFloat -> SyntaxKind.FloatKeyword
    | TInt -> SyntaxKind.IntKeyword
    | TUInt -> SyntaxKind.UIntKeyword
    | TLong -> SyntaxKind.LongKeyword
    | TULong -> SyntaxKind.ULongKeyword
    | TObject -> SyntaxKind.ObjectKeyword
    | TShort -> SyntaxKind.ShortKeyword
    | TUShort -> SyntaxKind.UShortKeyword
    | TString -> SyntaxKind.StringKeyword

let rec getTypeSyntax = function
    | Common s -> SyntaxFactory.ParseTypeName s
    | Collection s ->
        SyntaxFactory.QualifiedName(
            SyntaxFactory.QualifiedName(
                SyntaxFactory.QualifiedName(
                    SyntaxFactory.IdentifierName "System",
                    SyntaxFactory.IdentifierName "Collections"
                ),
                SyntaxFactory.IdentifierName "Generic"
            ),
            SyntaxFactory.GenericName(
                SyntaxFactory.Identifier "IReadOnlyList",
                getTypeSyntax s
                |> SyntaxFactory.SingletonSeparatedList
                |> SyntaxFactory.TypeArgumentList
            )
        )
        :> TypeSyntax
    | Predefined csType ->
        csType
        |> toSyntaxKind
        |> SyntaxFactory.Token
        |> SyntaxFactory.PredefinedType
        :> TypeSyntax
    | Optional t ->
        getTypeSyntax t
        |> SyntaxFactory.NullableType
        :> TypeSyntax
