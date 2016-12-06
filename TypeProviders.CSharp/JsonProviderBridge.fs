namespace TypeProviders.CSharp

open ProviderImplementation

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
    open System.Reflection
    open ProviderImplementation.ProvidedTypes

    let createProvider() =
        new JsonProvider(
            TypeProviderHost.CreateConfig(
                Assembly.GetExecutingAssembly(),
                []
            )
        )

    let createParametricRootType (typeProvider: TypeProviderForNamespaces) (args: JsonProviderArgs) =
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
        use provider = createProvider()

        let rootType = createParametricRootType provider args

        TypeProviderParser.parseDataType rootType
