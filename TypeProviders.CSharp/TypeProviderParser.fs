module TypeProviders.CSharp.TypeProviderParser

open System
open System.Reflection
open FSharp.Data
open ProviderImplementation.ProvidedTypes
open TypeProviders.CSharp

let parseDataType getTypeMembers (rootType: ProvidedTypeDefinition) =
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
                    getTypeMembers t
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
