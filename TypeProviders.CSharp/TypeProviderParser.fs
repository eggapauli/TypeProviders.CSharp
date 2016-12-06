module TypeProviders.CSharp.TypeProviderParser

open System
open System.Reflection
open FSharp.Data
open ProviderImplementation.ProvidedTypes
open TypeProviders.CSharp

let getMembers (ty: Type) =
    let isGetterOrSetter m =
        ty.GetProperties()
        |> Seq.exists(fun p -> p.GetGetMethod() = m || p.GetSetMethod() = m)
    [
        yield! ty.GetProperties() |> Seq.cast<MemberInfo>
        yield! ty.GetNestedTypes() |> Seq.cast<MemberInfo>
    ]
    |> List.filter (function
        | :? PropertyInfo as p when p.PropertyType.FullName = typeof<JsonValue>.FullName -> false
        | :? MethodInfo as m when isGetterOrSetter m -> false
        | _ -> true
    )

let parseDataType (rootType: ProvidedTypeDefinition) =
    let returnType =
            rootType.GetMethod("Parse").ReturnType
            |> TypeName.fromType

    let getChildTypeDefinition ty =
        let rec getChildMemberDefinition (m: MemberInfo) =
            match m with
            | :? PropertyInfo as p ->
                Property (p.Name, TypeName.fromType p.PropertyType)
            | :? Type as t ->
                let members =
                    getMembers t
                    |> List.map getChildMemberDefinition
                SubType (t.Name, members)
            | _ -> failwithf "Unexpected type member: %s" (m.GetType().FullName)

        getChildMemberDefinition ty

    let members =
        rootType.GetNestedTypes()
        |> Seq.map getChildTypeDefinition
        |> Seq.toList

    {
        ReturnTypeFromParsingData = returnType
        Members = members
    }
