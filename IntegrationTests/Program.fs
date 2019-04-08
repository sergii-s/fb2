// Learn more about F# at http://fsharp.org

open System
open IncrementalBuild

[<EntryPoint>]
let main argv =
    let incrementalBuild = FB2.getIncrementalBuild (fun p -> 
        { p with 
            Repository = "C:\\Dev\\antvoice" 
            Storage = SnaphotStorage.FileSystem "C:\\temp\\snapshots"
        })
    
    incrementalBuild
    |> FB2.restoreSnapshot

    incrementalBuild
    |> FB2.createSnapshot BuildConfiguration.Debug

    0 // return an integer exit code
