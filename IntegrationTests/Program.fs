// Learn more about F# at http://fsharp.org

open System
open IncrementalBuild

let inline (>>>) f1 f2 = fun x -> f1(x); f2(x)
let publish (project:Project) = 
  printfn "Publishing app"
let docker app (project:Project) = 
  printfn "Docker app"
let zipPublished app (project:Project) = 
  printfn "Docker app"

[<EntryPoint>]
let main argv =
    
    let apps = 
      [|
        Application.dotnet "CacheLoader"
          {
              DependsOn =  [|"kubernetes/promising-products-loader"|]
              Publish = publish >>> docker "antvoice-test/cache-loader"
              Deploy = ignore
          }
        Application.dotnet "OneShot"
          {
              DependsOn = [||]
              Publish = publish >>> docker "antvoice-test/oneshot"
              Deploy = ignore
          }
        Application.dotnet "MazeberryExporter"
          {
              DependsOn = [||]
              Publish = publish >>> docker "antvoice-test/mazeberry"
              Deploy = ignore
          }
        // CUSTOM
        Application.custom "commander-ui" "AntVoice.Web/AntVoice.Commander/commander"
          {
              DependsOn = [||]
              Publish = ignore 
              Deploy = ignore
          }     
      |] 
    let incrementalBuild = apps |> FB2.getIncrementalBuild (fun p -> 
        { p with 
            Repository = "/home/sergii/dev/antvoice" 
            Storage = SnaphotStorage.FileSystem "/home/sergii/.fb2"
        })
    
    incrementalBuild
    |> FB2.restoreSnapshot

    incrementalBuild
    |> FB2.createSnapshot BuildConfiguration.Debug

    0 // return an integer exit code
