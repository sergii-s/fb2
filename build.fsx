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
open Fake.Core
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.DotNet

let solution = "./fb2.sln"
let artifacts = "./artifacts"

let nugetSource = Environment.environVarOrDefault "NUGET_SOURCE" "https://api.nuget.org/v3/index.json"
let nugetKey = Environment.environVarOrNone "NUGET_KEY"

let version = sprintf "0.11.%s" (Environment.environVar "BUILD_NUMBER")

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

Target.create "Test" (fun _ ->
  solution
    |> DotNet.test (fun p ->
        { p with
            NoRestore = true
            NoBuild = true
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
  let outputPath = Environment.environVarOrFail "OUTPUT_PATH"
  DotNet.exec id "nuget" (sprintf "push %s/*.nupkg -s %s" artifacts outputPath)
    |> ignore
)

Target.create "Nuget-publish" (fun _ ->
  match nugetKey with
  | Some nugetKey ->
    DotNet.exec id "nuget" (sprintf "push artifacts/*.nupkg -k %s -s %s" nugetKey nugetSource)
      |> ignore
  | None ->
    failwith "You should precise 'nugetKey' environment variable to publish nuget"
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
  ==> "Test"
  ==> "Default"

"Nuget-package"
  ==> "Nuget-publish"

"Nuget-package"
  ==> "Nuget-publish-local"

// *** Start Build ***
Target.runOrDefault "Default"
