#I @"..\packages"
#r @"FAKE\tools\FakeLib.dll"

open System
open Fake
open Testing.XUnit2
open PaketTemplate

let repositoryBasePath = __SOURCE_DIRECTORY__ @@ ".."
let buildBasePath = repositoryBasePath @@ "build"
let artifactsPath = repositoryBasePath @@ "artifacts"
let toolsBaseDirectory = repositoryBasePath @@ "packages" @@ "build"

let assemblyVersion = getBuildParam "GitVersion_AssemblySemVer"
let nuGetVersion = getBuildParam "GitVersion_NuGetVersion"
let commitId = getBuildParam "GitVersion_Sha"

let projectDescription = "Generate types out of untyped data (e.g. JSON)."

Target "Clean" <| fun () ->
    CleanDirs [ buildBasePath; artifactsPath ]

let slnPath = FullName "TypeProviders.CSharp.sln"

let backup file =
    tracefn "Backing up %s" file
    let backupPath = sprintf "%s.BACKUP" file
    CopyFile backupPath file
    { new IDisposable with
        member x.Dispose() =
            tracefn "Restoring %s" file
            DeleteFile file
            Rename file backupPath
    }

module CleanUp =
    let mutable private cleanUpDisposables = []
    let register (d: IDisposable) =
        cleanUpDisposables <- d :: cleanUpDisposables
        ActivateFinalTarget "CleanUp"

    FinalTarget "CleanUp" <| fun () ->
        cleanUpDisposables
        |> List.iter (fun d -> d.Dispose())

Target "PatchBuildTimeGenerationFiles" <| fun () ->
    let globalsFile = "TypeProviders.CSharp.BuildTimeGeneration.Attributes" @@ "Globals.cs" |> FullName
    backup globalsFile |> CleanUp.register
    let globalsTemplate = sprintf """namespace TypeProviders.CSharp.BuildTimeGeneration.Attributes
{
    internal static class Globals
    {
        public const string AssemblyVersion = "%s";
    }
}"""
    System.IO.File.WriteAllText(globalsFile, globalsTemplate assemblyVersion)

module Xml =
    open System.Xml
    let modify (fileName: string) fn =
        let doc = new XmlDocument()
        doc.Load fileName
        let doc: XmlDocument = fn doc
        doc.Save fileName

Target "PatchCodeRefactoringProviderFiles" <| fun () ->
    let vsixManifestPath = "TypeProviders.CSharp.CodeRefactoringProvider.Vsix" @@ "source.extension.vsixmanifest" |> FullName
    backup vsixManifestPath |> CleanUp.register
    let modifyFn =
        let namespaces = ["x", "http://schemas.microsoft.com/developer/vsx-schema/2011"]
        let replaceNode xPath value = XPathReplaceNS xPath value namespaces
        XPathReplaceNS "/x:PackageManifest/x:Metadata/x:Identity/@Version" assemblyVersion namespaces
        >> XPathReplaceInnerTextNS "/x:PackageManifest/x:Metadata/x:Description" projectDescription namespaces
    Xml.modify vsixManifestPath modifyFn

let test =
    let setXUnitParams (p: XUnit2Params) =
        { p with
            ErrorLevel = Error
            Parallel = All
            ToolPath = toolsBaseDirectory @@ "xunit.runner.console" @@ "tools" @@ "xunit.console.exe"
        }
    Seq.singleton >> xUnit2 setXUnitParams

Target "TestCore" <| fun () ->
    let buildPath = buildBasePath @@ "test" @@ "TypeProviders.CSharp.Test"
    let target = "TypeProviders_CSharp_Test"
    MSBuildRelease buildPath target [ slnPath ] |> ignore
    !!(buildPath @@ "*.Test.dll")
    |> Seq.exactlyOne
    |> test

Target "TestBuildTimeGeneration" <| fun () ->
    let buildPath = buildBasePath @@ "test" @@ "TypeProviders.CSharp.BuildTimeGeneration.Test"
    let target = @"BuildTimeGeneration\Test\TypeProviders_CSharp_BuildTimeGeneration_Test"
    let properties _ = [
        "GeneratorAssemblyBaseSearchPath", buildPath
    ]
    MSBuildWithProjectProperties buildPath target properties [ slnPath ] |> ignore
    !!(buildPath @@ "*.Test.dll")
    |> Seq.exactlyOne
    |> test

Target "TestCodeRefactoringProvider" <| fun () ->
    let buildPath = buildBasePath @@ "test" @@ "TypeProviders.CSharp.CodeRefactoringProvider.Test"
    let target = @"CodeRefactoringProvider\TypeProviders_CSharp_CodeRefactoringProvider_Test"
    MSBuildRelease buildPath target [ slnPath ] |> ignore
    !!(buildPath @@ "*.Test.dll")
    |> Seq.exactlyOne
    |> test

Target "CreateCodeRefactoringProviderVsix" <| fun () ->
    let buildPath = buildBasePath @@ "CodeRefactoringProvider"
    MSBuildRelease buildPath "CodeRefactoringProvider\TypeProviders_CSharp_CodeRefactoringProvider_Vsix" [ slnPath ] |> ignore
    !! "*.vsix"
    |> SetBaseDir buildPath
    |> Seq.exactlyOne
    |> CopyFile artifactsPath

Target "CreateBuildTimeGenerationNuGetPackage" <| fun () ->
    let buildBasePath = buildBasePath @@ "BuildTimeGeneration"

    let targets =
        [
            @"BuildTimeGeneration\TypeProviders_CSharp_BuildTimeGeneration"
            @"BuildTimeGeneration\TypeProviders_CSharp_BuildTimeGeneration_Attributes"
        ]
        |> String.concat ";"

    let buildPath = buildBasePath @@ "msbuild"
    MSBuildRelease buildPath targets [ slnPath ] |> ignore

    let paketBasePath = buildBasePath @@ "paket"
    let paketTemplateFilePath = paketBasePath @@ "paket.template"
    let setPaketTemplateParams (p: PaketTemplateParams) =
        {
            p with 
                TemplateFilePath = Some paketTemplateFilePath
                TemplateType = File
                Id = Some "TypeProviders.CSharp.BuildTimeGeneration"
                Version = Some nuGetVersion
                Authors = [ "Johannes Egger" ]
                Description = [ projectDescription ]
                DevelopmentDependency = Some true
                LicenseUrl = Some (sprintf "https://github.com/eggapauli/TypeProviders.CSharp/blob/%s/LICENSE" commitId)
                ProjectUrl = Some "https://github.com/eggapauli/TypeProviders.CSharp"
                Dependencies =
                    [
                        "CodeGeneration.Roslyn.BuildTime", GreaterOrEqualSafe LOCKEDVERSION
                    ]
                Files =
                    [
                        Include ((buildPath @@ "ILRepack" @@ "*.*"), "tools")
                        Include ((buildPath @@ "TypeProviders.CSharp.BuildTimeGeneration.Attributes.*"), "tools")
                        Include ((repositoryBasePath @@ "NuGet" @@ "TypeProviders.CSharp.BuildTimeGeneration.props"), "build")
                        Include ((buildPath @@ "TypeProviders.CSharp.BuildTimeGeneration.Attributes.*"), "lib/netstandard1.1")
                    ]
        }

    directory paketTemplateFilePath |> ensureDirectory 
    PaketTemplate setPaketTemplateParams

    let setPaketPackParams (p: Paket.PaketPackParams) =
        {
            p with
                ToolPath = repositoryBasePath @@ ".paket" @@ "paket.exe"
                TemplateFile = paketTemplateFilePath
                OutputPath = artifactsPath
                //Symbols = true // doesn't work with `TemplateType = File`, but we don't really care about symbols for this package
        }
    Paket.Pack setPaketPackParams

Target "Default" <| fun () ->
    printfn "Build succeeded. Output path: %s" buildBasePath

"Default" <== [ "CreateCodeRefactoringProviderVsix"; "CreateBuildTimeGenerationNuGetPackage" ]
"CreateCodeRefactoringProviderVsix" <== [ "TestCodeRefactoringProvider" ]
"CreateBuildTimeGenerationNuGetPackage" <== [ "TestBuildTimeGeneration" ]
"TestCodeRefactoringProvider" <== [ "TestCore"; "PatchCodeRefactoringProviderFiles" ]
"TestBuildTimeGeneration" <== [ "TestCore"; "PatchBuildTimeGenerationFiles" ]
"TestCore" <== [ "Clean" ]
"PatchCodeRefactoringProviderFiles" <== [ "Clean" ]
"PatchBuildTimeGenerationFiles" <== [ "Clean" ]

RunTargetOrDefault "Default"
