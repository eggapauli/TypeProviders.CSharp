#r @"lib\FAKE\tools\FakeLib.dll"

open System
open Fake
open NuGetHelper
open Testing.XUnit2

let basePath = FullName "build"

let getVersion() =
    getBuildParam "GitVersion_AssemblySemVer"

Target "Clean" (fun () ->
    DeleteDir basePath
)

let backup file =
    let backupPath = sprintf "%s.BACKUP" file
    CopyFile backupPath file
    { new IDisposable with
        member x.Dispose() =
            DeleteFile file
            Rename file backupPath
    }

let slnPath = FullName "TypeProviders.CSharp.sln"

Target "Build" (fun () ->
    let vsixManifestPath = "TypeProviders.CSharp.Vsix" @@ "source.extension.vsixmanifest" |> FullName
    use x = backup vsixManifestPath
    XmlPokeNS vsixManifestPath ["x", "http://schemas.microsoft.com/developer/vsx-schema/2011"] "/x:PackageManifest/x:Metadata/x:Identity/@Version" (getVersion())

    let buildPath = basePath @@ "bin"
    MSBuildRelease buildPath "TypeProviders_CSharp_Vsix:Rebuild" [ slnPath ] |> ignore
)

Target "Test" (fun () ->
    let buildPath = basePath @@ "test"
    MSBuildRelease buildPath "TypeProviders_CSharp_Test:Rebuild" [ slnPath ] |> ignore

    let setParams (p: XUnit2Params) =
        { p with
            ErrorLevel = Error
            Parallel = All
            ToolPath = __SOURCE_DIRECTORY__ @@ "lib" @@ "xunit.runner.console" @@ "tools" @@ "xunit.console.exe"
        }
    xUnit2 setParams [ buildPath @@ "TypeProviders.CSharp.Test.dll" ]
)

Target "Publish" (fun () ->
    let artifactPath = basePath @@ "bin" @@ "TypeProviders.CSharp.vsix" |> FullName
    printfn "Build succeeded. Vsix can be found at %s" artifactPath
)

"Publish" <== [ "Test" ]
"Test" <== [ "Build" ]
"Build" <== [ "Clean" ]

RunTargetOrDefault "Publish"
