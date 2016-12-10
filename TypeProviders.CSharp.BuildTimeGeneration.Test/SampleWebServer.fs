module TypeProviders.CSharp.BuildTimeGeneration.Test.SampleWebServer

open System
open System.Net
open System.Threading
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful

let setup response =
    let app =
        choose
            [ GET >=> choose
                [ path "/" >=> OK response ]
            ]

    let cts = new CancellationTokenSource()
    let config =
        { defaultConfig with
            cancellationToken = cts.Token
            bindings =
                [
                    HttpBinding.mk Protocol.HTTP IPAddress.Loopback 6666us
                ]
        }

    let (listening, server) = startWebServerAsync config app
    Async.Start (server, cts.Token)
    listening |> Async.RunSynchronously |> ignore

    { new IDisposable with
        member x.Dispose() =
            cts.Cancel()
            cts.Dispose()
    }
