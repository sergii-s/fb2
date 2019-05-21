// Learn more about F# at http://fsharp.org

open System
open IncrementalBuild

let inline (>>>) f1 f2 = fun x ->
    let res = f1(x)
    if res = 0 then f2(x) else res 

let publish (project:Project) = 
  printfn "Publishing app"
  0

let docker app (project:Project) = 
  printfn "Docker app"
  0
  
let zipPublished app (project:Project) = 
  printfn "Docker app"
  0

[<EntryPoint>]
let main argv =
    
    let apps = 
      [|
        Application.dotnet "CacheLoader"
          {
              DependsOn =  [|"kubernetes/promising-products-loader"|]
              Publish = publish >>> docker "antvoice-test/cache-loader"
              Deploy = Application.NoDeployment
          }
        Application.dotnet "OneShot"
          {
              DependsOn = [||]
              Publish = publish >>> docker "antvoice-test/oneshot"
              Deploy = Application.NoDeployment
          }
        Application.dotnet "MazeberryExporter"
          {
              DependsOn = [||]
              Publish = publish >>> docker "antvoice-test/mazeberry"
              Deploy = Application.NoDeployment
          }
        // CUSTOM
        Application.custom "commander-ui" "AntVoice.Web/AntVoice.Commander/commander"
          {
              DependsOn = [||]
              Publish = Application.NoPublish
              Deploy = Application.NoDeployment
          }     
      |] 
    let incrementalBuild = apps |> FB2.getIncrementalBuild "1.0.0" (fun p -> 
        { p with 
            Repository = "/home/sergii/dev/antvoice" 
            Storage = SnaphotStorage.FileSystem "/home/sergii/.fb2"
        })
    
    incrementalBuild
    |> FB2.restoreSnapshot

    incrementalBuild
    |> FB2.createSnapshot BuildConfiguration.Debug

    0 // return an integer exit code
