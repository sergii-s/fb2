namespace IncrementalBuild
open System.IO
open FSharp.Data
open Graph

type SourceControlProvider =
    | Git

type SnaphotStorage =
    | FileSystem of string
    | GoogleCloudStorage of string
        
type BuildParameters = {
    Repository : string
    SourceControlProvider : SourceControlProvider
    Storage : SnaphotStorage
    MaxCommitsCheck : int
}
    
type IncrementalBuildInfo = {
    Id : string
    DiffId : string option
    Parameters : BuildParameters
    ProjectStructure : ProjectStructure
    ImpactedProjectStructure : ProjectStructure
//    NotImpactedProjectStructure : ProjectStructure
}

    
type BuildConfiguration =
    | Release
    | Debug

type SnaphotDescription = JsonProvider<""" { "apps" : [{ "app":"bidder", "snapshot":"sjg8sjen" }] }""">

module SnapshotStorage =
    let findSnapshot storage = 
        match storage with 
        | FileSystem path -> FileSystemSnapshotStorage.firstAvailableSnapshot path
        | GoogleCloudStorage bucket -> GCSSnapshotStorage.firstAvailableSnapshot bucket
    let getSnapshot storage =
        match storage with 
        | FileSystem path -> FileSystemSnapshotStorage.getSnapshot path
        | GoogleCloudStorage bucket -> GCSSnapshotStorage.getSnapshot bucket
    let saveSnapshot storage =
        match storage with 
        | FileSystem path -> FileSystemSnapshotStorage.saveSnapshot path
        | GoogleCloudStorage bucket -> GCSSnapshotStorage.saveSnapshot bucket

    
module FB2 =
    open System
    
    let private defaultParameters = {
        Repository = "."
        SourceControlProvider = SourceControlProvider.Git
        Storage =
            Environment.SpecialFolder.Personal
            |> Environment.GetFolderPath
            |> sprintf "%s/.fb2"
            |> SnaphotStorage.FileSystem 
        MaxCommitsCheck = 20
    }

    let createSnapshot configuration build =
        let conf =
            match configuration with
            | Release -> "Release"
            | Debug -> "Debug"
        let zipTemporaryPath = (sprintf "%s/%s.zip" (Path.GetTempPath()) build.Id)
        printfn "Temporary zip %s" zipTemporaryPath
        build.ProjectStructure.Projects
        |> Seq.collect (fun p -> seq {
            yield! Directory.EnumerateFiles(sprintf "%s/bin/%s/%s/" p.ProjectFolder conf p.TargetFramework, "*.*")
            yield! Directory.EnumerateFiles(sprintf "%s/obj/%s/%s/" p.ProjectFolder conf p.TargetFramework, "*.*")
        })
        |> Zip.zip build.ProjectStructure.RootFolder zipTemporaryPath
        |> SnapshotStorage.saveSnapshot build.Parameters.Storage

    let restoreSnapshot build =
        match build.DiffId with
        | Some snapshotId -> 
            let snapshotFile = snapshotId |> SnapshotStorage.getSnapshot build.Parameters.Storage
            snapshotFile |> Zip.unzip build.ProjectStructure.RootFolder
        | None -> printfn "Last build not found. Nothing to restore"
    
    let getIncrementalBuild parametersBuilder applications =
        let parameters = defaultParameters |> parametersBuilder
            
        let projectStructure = 
            parameters.Repository 
            |> Graph.readProjectStructure applications
        let commitIds = Git.getCommits projectStructure.RootFolder parameters.MaxCommitsCheck
        let currentCommitId = commitIds |> Array.head
        
        let lastSnapshotCommit =
            commitIds
            |> SnapshotStorage.findSnapshot parameters.Storage 
        
        match lastSnapshotCommit with
        | Some commitId ->
            let modifiedFiles = Git.getDiffFiles projectStructure.RootFolder currentCommitId commitId
            let impactedProjectStructure = modifiedFiles |> Graph.getImpactedProjects projectStructure 
//            let notImpactedProjects = projectStructure.Projects
//                                       |> Array.except impactedProjects
            printfn "Last snapshot %s. Impacted %i of %i projects. Impacted %i of %i applications"
                commitId
                impactedProjectStructure.Projects.Length projectStructure.Projects.Length
                impactedProjectStructure.Applications.Length projectStructure.Applications.Length
            printfn "Current snapshot id %s" currentCommitId
            {
                 Id = currentCommitId
                 DiffId = Some commitId
                 ProjectStructure = projectStructure
                 ImpactedProjectStructure = impactedProjectStructure
                 Parameters = parameters
            }
        | None ->
            printfn "Last snapshot is not found. Full build should be done"
            commitIds |> String.join "," |> printfn "Checked commit ids : %s"
            printfn "Current snapshot id %s" currentCommitId
            {
                 Id = currentCommitId
                 DiffId = None
                 ProjectStructure = projectStructure
                 ImpactedProjectStructure = projectStructure
                 Parameters = parameters
            }

    let createView name folder projects =
        name
            |> sprintf "%s.sln"
            |> Pathes.combine folder
            |> Pathes.safeDelete 
         
        ProcessHelper.run "dotnet" (sprintf "new sln --name %s" name) folder |> ignore
        ProcessHelper.run "dotnet" (sprintf "sln %s.sln add %s" name (projects |> String.concat " ")) folder |> ignore
    
    let publish structure =
        structure.Applications
        |> Array.map (fun app -> 
            match app.Parameters with
            | DotnetApplication dotnetApp ->
                let project = structure.Projects |> Array.find (fun p -> p.Name = app.Name)
                async { project |> dotnetApp.Publish }
            | CustomApplication customApp ->
                async { customApp.RootFolder |> customApp.Publish }
        )
    let deploy structure =
        structure.Applications
        |> Array.map (fun app -> 
            match app.Parameters with
            | DotnetApplication dotnetApp ->
                let project = structure.Projects |> Array.find (fun p -> p.Name = app.Name)
                async { project |> dotnetApp.Deploy }
            | CustomApplication customApp ->
                async { customApp.RootFolder |> customApp.Deploy }
        )        