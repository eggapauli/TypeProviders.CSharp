namespace TypeProviders.CSharp

open ProviderImplementation

module XmlProviderArgs =
    let create sample = {
        Sample = sample
        SampleIsList = false
        Global = false
        Culture = ""
        Encoding = ""
        ResolutionFolder = ""
        EmbeddedResource = ""
        InferTypesFromValues = true
    }

module XmlProviderBridge =
    open System
    open System.Reflection
    open ProviderImplementation.ProvidedTypes

    let createProvider() =
        new XmlProvider(
            TypeProviderHost.CreateConfig(
                Assembly.GetExecutingAssembly(),
                [ typeof<System.Xml.Linq.XElement>.Assembly ]
            )
        )

    let createParametricRootType (typeProvider: TypeProviderForNamespaces) (args: XmlProviderArgs) =
        let rootType =
            typeProvider.Namespaces
            |> Seq.head
            |> snd
            |> Seq.head
        let providerArgsArray: obj array =
            [|
                args.Sample
                args.SampleIsList
                args.Global
                args.Culture
                args.Encoding
                args.ResolutionFolder
                args.EmbeddedResource
                args.InferTypesFromValues
            |]

        rootType.MakeParametricType("root", providerArgsArray)

    let parseDataType args =
        use provider = createProvider()

        let rootType = createParametricRootType provider args

        let getTypeMembers (ty: Type) =
            [
                yield! ty.GetProperties() |> Seq.cast<MemberInfo>
                yield! ty.GetNestedTypes() |> Seq.cast<MemberInfo>
            ]
            |> List.filter (function
                | :? PropertyInfo as p when p.PropertyType.FullName = typeof<System.Xml.Linq.XElement>.FullName -> false
                | :? PropertyInfo as p when p.DeclaringType.FullName = typeof<FSharp.Data.Runtime.BaseTypes.XmlElement>.FullName -> false
                | :? PropertyInfo as p -> true
                | :? Type -> true
                | _ -> false
            )

        TypeProviderParser.parseDataType getTypeMembers rootType
