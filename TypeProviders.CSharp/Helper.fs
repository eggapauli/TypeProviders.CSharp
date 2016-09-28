namespace TypeProviders.CSharp

module Option =
    let ifNone v o =
        defaultArg o v

    let cast<'T when 'T : null> (o: obj) = 
        match o with
        | :? 'T as res -> Some res
        | _ -> None

module Async =
    open System.Threading
    open System.Threading.Tasks

    let awaitTaskAllowContextSwitch (task: Task<'a>) =
        async {
            use handle = new SemaphoreSlim(0)
            let awaiter = task.ConfigureAwait(false).GetAwaiter()
            awaiter.OnCompleted(fun () -> ignore (handle.Release()))
            do! handle.AvailableWaitHandle |> Async.AwaitWaitHandle |> Async.Ignore
            return awaiter.GetResult()
        }

module Seq =
    let ofType<'a> (source : System.Collections.IEnumerable) : seq<'a> =
        let resultType = typeof<'a>
        seq {
            for item in source do
                match item with
                | null -> ()
                | _ ->
                    if resultType.IsAssignableFrom <| item.GetType()
                    then yield (downcast item)
        }
