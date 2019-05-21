open Fake.DotNet.NuGet
open Fake.SystemHelper
open Fake.DotNet
#r "paket:
storage: none
source https://api.nuget.org/v3/index.json

nuget Fake.Core.Target
nuget Fake.IO.FileSystem
nuget Fake.DotNet.Cli
nuget Fake.DotNet.Nuget
nuget Fake.DotNet.AssemblyInfoFile  //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.DotNet

let solution = "./fb2.sln"
let artifacts = "./artifacts"

let nugetSource = "https://api.nuget.org/v3/index.json"
let nugetKey = ""

let version = Environment.environVarOrDefault "BUILD_NUMBER" "0.10.3"
let buildConfiguration = 
  if Environment.environVarOrDefault "BUILD_CONF" "Release" = "Release"
    then 
      DotNet.BuildConfiguration.Release 
    else 
      DotNet.BuildConfiguration.Debug 

// *** Define Targets ***
Target.create "Clean" (fun _ ->
  solution 
    |> DotNet.exec id "clean"  
    |> ignore

  artifacts 
    |> Shell.cleanDir

  artifacts
    |> Shell.mkdir
)

Target.create "SetVersion" (fun _ ->
  AssemblyInfoFile.createCSharp "SolutionInfo.cs"
    [ AssemblyInfo.Version version
      AssemblyInfo.FileVersion version ]
)

Target.create "Restore" (fun _ ->
  solution
    |> DotNet.restore id
)

Target.create "Build" (fun _ ->
  solution
    |> DotNet.build (fun p -> 
        { p with 
            NoRestore = true
            Configuration = buildConfiguration
        })
)

Target.create "Nuget-package" (fun _ ->
  let projectFiles = !! "./**/fb2.fsproj"
  
  for projectFile in projectFiles do
      projectFile 
        |> DotNet.pack (fun p ->
            { p with
                OutputPath = Some(artifacts |> Path.getFullName)
                MSBuildParams = { MSBuild.CliArguments.Create () with Properties = ["Version", version]}
                NoBuild = true
                NoRestore = true
            })
)

Target.create "Nuget-publish-local" (fun _ ->
  DotNet.exec id "nuget" (sprintf "push %s/*.nupkg -s %s" artifacts "/home/sergii/dev/packages/")
    |> ignore
)

Target.create "Nuget-publish" (fun _ ->
  DotNet.exec id "nuget" (sprintf "push %s/*.nupkg -k %s -s %s" artifacts nugetKey nugetSource)
    |> ignore
)

Target.create "Default" (fun _ ->
  version 
    |> sprintf "Build %s done"
    |> Trace.trace
)

open Fake.Core.TargetOperators

// *** Define Dependencies ***
"Clean"
  ==> "SetVersion"
  ==> "Restore"
  ==> "Build"
  ==> "Default"
  ==> "Nuget-package"
  ==> "Nuget-publish"

"Nuget-package"
  ==> "Nuget-publish-local"
  
// *** Start Build ***
Target.runOrDefault "Default"
