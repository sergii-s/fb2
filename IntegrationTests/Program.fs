// Learn more about F# at http://fsharp.org

open System
open IncrementalBuild

[<EntryPoint>]
let main argv =
    let incrementalBuild = FB2.getIncrementalBuildStatus (fun p -> { p with Repository = "/home/sergii/dev/antvoice" })
    let assemblies =
        incrementalBuild.ProjectStructure.Projects
        |> Map.toSeq
        |> Seq.map snd
        |> FB2.getAssemblies FB2.Debug
        |> Array.ofSeq
    0 // return an integer exit code
