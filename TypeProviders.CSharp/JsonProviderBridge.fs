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
        use provider = createJsonProvider()

        let rootType = createParametricRootType provider args

        TypeProviderParser.parseDataType rootType
