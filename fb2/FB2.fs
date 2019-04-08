namespace IncrementalBuild
open System.IO

open Graph

type SourceControlProvider =
    | Git

type SnaphotStorage =
    | FileSystem of string
    | GoogleCloudStorage
        
type BuildParameters = {
    Repository : string
    SourceControlProvider : SourceControlProvider
    Storage : SnaphotStorage
    MaxCommitsCheck : int
}
    
type IncrementalBuildInfo = {
    Id : string
    DiffId : string option
    ProjectStructure : ProjectStructure
    ImpactedProjects : Project array
    NotImpactedProjects : Project array
    Parameters : BuildParameters
}
    
type BuildConfiguration =
    | Release
    | Debug

module SnapshotStorage =
    let findSnapshot storage = 
        match storage with 
        | FileSystem path -> FileSystemSnapshotStorage.firstAvailableSnapshot path
        | GoogleCloudStorage -> failwith "notimplemented"
    let getSnapshot storage =
        match storage with 
        | FileSystem path -> FileSystemSnapshotStorage.getSnapshot path
        | GoogleCloudStorage -> failwith "notimplemented"
    let saveSnapshot storage =
        match storage with 
        | FileSystem path -> FileSystemSnapshotStorage.saveSnapshot path
        | GoogleCloudStorage -> failwith "notimplemented"

    
module FB2 =
    
    let private defaultBuilder = {
        Repository = "."
        SourceControlProvider = SourceControlProvider.Git
        Storage = SnaphotStorage.FileSystem "/tmp/snapshots"
        MaxCommitsCheck = 20
    }

    let createSnapshot configuration build =
        let conf =
            match configuration with
            | Release -> "Release"
            | Debug -> "Debug"
        let zipTemporaryPath = (sprintf "%s/%s.zip" (Path.GetTempPath()) build.Id) 
        build.ProjectStructure.Projects
        |> Map.toSeq
        |> Seq.map snd
        |> Seq.collect (fun p -> seq {
            yield! Directory.EnumerateFiles(sprintf "%s/bin/%s/%s/" p.ProjectFolder conf p.TargetFramework, "*.*")
            yield! Directory.EnumerateFiles(sprintf "%s/obj/%s/%s/" p.ProjectFolder conf p.TargetFramework, "*.*")
        })
        |> Zip.zip build.ProjectStructure.RootFolder zipTemporaryPath
        |> SnapshotStorage.saveSnapshot build.Parameters.Storage

    let restoreSnapshot build =
        let snapshotId = build.DiffId.Value
        let snapshotFile = snapshotId |> SnapshotStorage.getSnapshot build.Parameters.Storage
        snapshotFile |> Zip.unzip build.ProjectStructure.RootFolder
    
    let getIncrementalBuild parametersBuilder =
        let parameters = defaultBuilder |> parametersBuilder
            
        let projectStructure = 
            parameters.Repository 
            |> Graph.readProjectStructure
        let commitIds = Git.getCommits projectStructure.RootFolder parameters.MaxCommitsCheck
        let currentCommitId = commitIds |> Array.head
        
        let lastSnapshotCommit =
            commitIds
            |> SnapshotStorage.findSnapshot parameters.Storage 
        
        match lastSnapshotCommit with
        | Some commitId ->
            let modifiedFiles = Git.getDiffFiles projectStructure.RootFolder currentCommitId commitId
            let impactedProjects = modifiedFiles |> Graph.getImpactedProjects projectStructure |> Array.ofSeq
            let notImpactedProjects = projectStructure.Projects
                                       |> Map.toArray
                                       |> Array.map snd
                                       |> Array.except impactedProjects
            printfn "Last snapshot %s. Build %i of %i projects" commitId impactedProjects.Length notImpactedProjects.Length
            {
                 Id = currentCommitId
                 DiffId = Some commitId
                 ProjectStructure = projectStructure
                 ImpactedProjects = impactedProjects
                 NotImpactedProjects = notImpactedProjects
                 Parameters = parameters
            }
        | None ->
            printfn "Last snapshot is not found. Full build should be done"
            {
                 Id = currentCommitId
                 DiffId = None
                 ProjectStructure = projectStructure
                 ImpactedProjects = projectStructure.Projects
                                       |> Map.toArray
                                       |> Array.map snd
                 NotImpactedProjects = [||]
                 Parameters = parameters
            }

    let createView name folder projects =
        name
            |> sprintf "%s.sln"
            |> Pathes.combine folder
            |> Pathes.safeDelete 
         
        ProcessHelper.run "dotnet" (sprintf "new sln --name %s" name) folder |> ignore
        ProcessHelper.run "dotnet" (sprintf "sln %s.sln add %s" name (projects |> String.concat " ")) folder |> ignore
