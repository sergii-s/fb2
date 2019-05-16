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
    Version : string
    DiffId : string option
    Parameters : BuildParameters
    ProjectStructure : ProjectStructure
    ImpactedProjectStructure : ProjectStructure
}

type SnaphotDescription = JsonProvider<""" { 
    "id" : "asdlkfds",
    "apps" : [{ "app":"bidder", "snapshot":"sjg8sjen", "version":"1.0.0" }]
}""">

type BuildConfiguration =
    | Release
    | Debug

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


module FileStructure =
    
    let getWorkingFolder build =
        sprintf "%s/.fb2/" build.ProjectStructure.RootFolder
    
    let getSnapshotDescriptionFilePath build =
        build
            |> getWorkingFolder
            |> sprintf "%s/applications.json" 
    
    let ensureWorkingFolder build =
        let workingFolder = build |> getWorkingFolder
        if workingFolder |> Directory.Exists |> not then
            workingFolder |> Directory.CreateDirectory |> ignore
    
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
    
    let private updateSnapshotDescription build =
        build |> FileStructure.ensureWorkingFolder
        let snapshotDescriptionFile =
            build |> FileStructure.getSnapshotDescriptionFilePath
        
        let snapshotDescription =
            if snapshotDescriptionFile |> File.Exists then
                let oldSnapshotDescription = 
                    snapshotDescriptionFile 
                    |> SnaphotDescription.Load
                let apps = 
                    build.ProjectStructure.Applications
                    |> Array.map (fun app -> 
                        match build.ImpactedProjectStructure.Applications |> Array.tryFind (fun app' -> app'.Name = app.Name) with
                        | Some app -> SnaphotDescription.App(app.Name, build.Id, build.Version)
                        | None -> oldSnapshotDescription.Apps |> Array.find (fun app' -> app'.App = app.Name)
                    )
                    |> Array.ofSeq
                SnaphotDescription.Root(build.Id, apps)       
            else
                let apps = 
                    build.ImpactedProjectStructure.Applications
                    |> Array.map (fun p -> 
                        SnaphotDescription.App(p.Name, build.Id, build.Version)
                    )
                SnaphotDescription.Root(build.Id, apps)       
            
        use writer = snapshotDescriptionFile |> File.CreateText 
        snapshotDescription.JsonValue.WriteTo(writer, JsonSaveOptions.None)

    let createSnapshot configuration build =
        let conf =
            match configuration with
            | Release -> "Release"
            | Debug -> "Debug"
        let zipTemporaryPath = (sprintf "%s/%s.zip" (Path.GetTempPath()) build.Id)
        printfn "Temporary zip %s" zipTemporaryPath
        build |> updateSnapshotDescription
        build.ProjectStructure.Projects
        |> Seq.collect (fun p -> seq {
            yield! Directory.EnumerateFiles(sprintf "%s/bin/%s/%s/" p.ProjectFolder conf p.TargetFramework, "*.*")
            yield! Directory.EnumerateFiles(sprintf "%s/obj/%s/%s/" p.ProjectFolder conf p.TargetFramework, "*.*")
        })
        |> Seq.append [build |> FileStructure.getSnapshotDescriptionFilePath]
        |> Zip.zip build.ProjectStructure.RootFolder zipTemporaryPath
        |> SnapshotStorage.saveSnapshot build.Parameters.Storage

    let restoreSnapshot build =
        match build.DiffId with
        | Some snapshotId -> 
            let snapshotFile = snapshotId |> SnapshotStorage.getSnapshot build.Parameters.Storage
            snapshotFile |> Zip.unzip build.ProjectStructure.RootFolder
        | None -> printfn "Last build not found. Nothing to restore"
    
    let getIncrementalBuild version parametersBuilder applications =
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
            printfn "Last snapshot %s. Impacted %i of %i projects. Impacted %i of %i applications"
                commitId
                impactedProjectStructure.Projects.Length projectStructure.Projects.Length
                impactedProjectStructure.Applications.Length projectStructure.Applications.Length
            printfn "Current snapshot id %s" currentCommitId
            {
                 Id = currentCommitId
                 Version = version
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
                 Version = version
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