namespace TypeProviders.CSharp.DataTypeUpdate

open TypeProviders.CSharp

module CSharp =
    let private getTypeRenameMap dataType =
        let getName = function
            | SubType (name, _) -> name
            | Property (name, _) -> name

        let getNames members =
            members
            |> List.map getName

        let rec getNonCollidingName originalName names =
            if List.contains originalName names
            then getNonCollidingName (sprintf "%s_" originalName) names
            else originalName

        let rec getTypeRenameMapForMember = function
            | SubType (name, members) ->
                let newName =
                    getNames members
                    |> getNonCollidingName name
                (name, newName) :: (List.collect getTypeRenameMapForMember members)
            | x -> []

        List.collect getTypeRenameMapForMember dataType.Members

    let ensureTypeHasNoPropertyWithSameName dataType =
        let typeRenameMap = getTypeRenameMap dataType |> Map.ofList

        let getNewName name =
            Map.tryFind name typeRenameMap
            |> Option.ifNone name

        let rec updatePropertyType = function
            | Common name -> getNewName name |> Common
            | Collection t -> updatePropertyType t |> Collection
            | Optional t -> updatePropertyType t |> Optional
            | Predefined t -> Predefined t

        let rec updateDataTypeMember = function
            | SubType (name, members) ->
                let newName = getNewName name
                let newMembers =
                    members
                    |> List.map updateDataTypeMember
                SubType (newName, newMembers)
            | Property (name, propertyType) ->
                Property (name, updatePropertyType propertyType)

        { dataType with Members = List.map updateDataTypeMember dataType.Members }