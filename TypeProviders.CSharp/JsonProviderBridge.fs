namespace TypeProviders.CSharp

type JsonProviderArgs = {
    Sample: string
    SampleIsList: bool
    RootName: string
    Culture: string
    Encoding: string
    ResolutionFolder: string
    EmbeddedResource: string
    InferTypesFromValues: bool
}

[<AutoOpen>]
module JsonProviderArgs_ =
    module JsonProviderArgs =
        let create sample = {
            Sample = sample
            SampleIsList = false
            RootName = ""
            Culture = ""
            Encoding = ""
            ResolutionFolder = ""
            EmbeddedResource = ""
            InferTypesFromValues = true
        }

module JsonProviderBridge =
    open System
    open ProviderImplementation
    open ProviderImplementation.ProvidedTypes
    open Microsoft.FSharp.Core.CompilerServices
    open System.Reflection

    type private ImportedBinaryMock(fileName) =
        member val FileName = fileName with get

    type private TcImportsMock
        (
            referencedDlls: ImportedBinaryMock list,
            tcImportsBase: TcImportsMock option
        ) =
        let dllInfos = referencedDlls
        member x.SystemRuntimeContainsType s =
             Type.GetType(s).Assembly.Equals typeof<System.Object>.Assembly.FullName

        member x.PrintDllInfos() =
             dllInfos
             |> Seq.iter (printfn "%O")

        member val Base = tcImportsBase

    let createJsonProvider() =
        let systemRuntimeContainsType =
            let dlls =
                AppDomain.CurrentDomain.GetAssemblies()
                |> Seq.filter (fun asm -> not asm.IsDynamic)
                |> Seq.map (fun asm -> ImportedBinaryMock asm.Location)
                |> Seq.toList
            let tcImports = TcImportsMock(dlls, TcImportsMock([], None) |> Some)
            fun t -> tcImports.SystemRuntimeContainsType t

        let cfg =
            TypeProviderConfig(
                systemRuntimeContainsType,
                RuntimeAssembly = Assembly.GetExecutingAssembly().FullName
            )

        new JsonProvider(cfg)

    let createParametricRootType (typeProvider: TypeProviderForNamespaces) args =
        let rootType =
            typeProvider.Namespaces
            |> Seq.head
            |> snd
            |> Seq.head
        let providerArgsArray: obj array =
            [|
                args.Sample
                args.SampleIsList
                args.RootName
                args.Culture
                args.Encoding
                args.ResolutionFolder
                args.EmbeddedResource
                args.InferTypesFromValues
            |]

        rootType.MakeParametricType("root", providerArgsArray)

    let parseDataType args =
        use provider = createJsonProvider()

        let rootType = createParametricRootType provider args

        let returnType =
                rootType.GetMethod("Parse").ReturnType
                |> TypeProviderBridge.getTypeName

        let getChildTypeDefinition provider ty =
            let rec getChildMemberDefinition (m: MemberInfo) =
                match m with
                | :? ProvidedProperty as p ->
                    Property (p.Name, TypeProviderBridge.getTypeName p.PropertyType)
                | :? ProvidedTypeDefinition as t ->
                    let members =
                        TypeProviderBridge.getMembers t
                        |> List.map getChildMemberDefinition
                    SubType (t.Name, members)
                | _ -> failwithf "Unexpected type member: %s" (m.GetType().FullName)

            getChildMemberDefinition ty

        let members = [
            yield!
                rootType.GetNestedTypes()
                |> Seq.map (getChildTypeDefinition provider)
        ]

        {
            ReturnTypeFromParsingData = returnType
            Members = members
        }

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

        let rec getTypeRenameMap' = function
            | SubType (name, members) ->
                let newName =
                    getNames members
                    |> getNonCollidingName name
                (name, newName) :: (List.collect getTypeRenameMap' members)
            | x -> []

        List.collect getTypeRenameMap' dataType.Members

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
