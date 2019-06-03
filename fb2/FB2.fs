namespace IncrementalBuild
open System
open System.IO
open FSharp.Data
open Graph
open Model

type SourceControlProvider =
    | Git

type SnaphotStorage =
    | FileSystem of string
    | GoogleCloudStorage of string
        
type BuildParameters = {
    Repository : string
    BaseBranches : string list
    SourceControlProvider : SourceControlProvider
    Storage : SnaphotStorage
    MaxCommitsCheck : int
}
    
type IncrementalBuildInfo = {
    Id : string
    Version : string
    Base : Snapshot option
    Branch : string
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
    
    let getWorkingFolder rootFolder =
        rootFolder |> sprintf "%s/.fb2" 
    
    let getSnapshotDescriptionFilePath rootFolder =
        rootFolder |> getWorkingFolder |> sprintf "%s/applications.json"
    
    let ensureWorkingFolder rootFolder =
        let workingFolder = rootFolder |> getWorkingFolder
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
        BaseBranches = []
    }
    
    let private updateSnapshotDescription build =
        build.ProjectStructure.RootFolder |> FileStructure.ensureWorkingFolder
        let snapshotDescriptionFile =
            build.ProjectStructure.RootFolder |> FileStructure.getSnapshotDescriptionFilePath
        
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
        let zipTemporaryPath = (sprintf "%s%s.zip" (Path.GetTempPath()) build.Id)
        printfn "Temporary zip %s" zipTemporaryPath
        build |> updateSnapshotDescription
        let zipFile =
            build.ProjectStructure.Projects
            |> Seq.collect (fun p -> seq {
                yield! Directory.EnumerateFiles(sprintf "%s/bin/%s/%s/" (Pathes.combine build.ProjectStructure.RootFolder p.ProjectFolder) conf p.TargetFramework, "*.*")
                yield! Directory.EnumerateFiles(sprintf "%s/obj/%s/%s/" (Pathes.combine build.ProjectStructure.RootFolder p.ProjectFolder) conf p.TargetFramework, "*.*")
            })
            |> Seq.append [build.ProjectStructure.RootFolder |> FileStructure.getSnapshotDescriptionFilePath]
            |> Zip.zip build.ProjectStructure.RootFolder zipTemporaryPath

        SnapshotStorage.saveSnapshot build.Parameters.Storage {Id = build.Id; Branch = build.Branch} zipTemporaryPath

    let restoreSnapshot build  =
        match build.Base with
        | Some snapshot -> 
            let snapshotFile = snapshot |> SnapshotStorage.getSnapshot build.Parameters.Storage
            snapshotFile |> Zip.unzip build.ProjectStructure.RootFolder
        | None -> printfn "Last build not found. Nothing to restore"
    
    let getIncrementalBuild version parametersBuilder applications =
        let parameters = defaultParameters |> parametersBuilder
            
        let projectStructure = 
            parameters.Repository 
            |> Graph.readProjectStructure applications
        let commitIds = Git.getCommits projectStructure.RootFolder parameters.MaxCommitsCheck 
        let branch = projectStructure.RootFolder |> Git.getBranch
        let currentCommitId = commitIds |> List.head
        
        printfn "Current commit id %s" currentCommitId
        printfn "Current branch %s" branch
        
        let baseSnapshot =
            commitIds
            |> SnapshotStorage.findSnapshot parameters.Storage (branch :: parameters.BaseBranches)
        
        match baseSnapshot with
        | Some snapshot ->
            let modifiedFiles = Git.getDiffFiles projectStructure.RootFolder currentCommitId snapshot.Id
            let impactedProjectStructure = modifiedFiles |> Graph.getImpactedProjects projectStructure 
            printfn "Last snapshot %s from branch %s. Impacted %i of %i projects. Impacted %i of %i applications"
                snapshot.Id
                snapshot.Branch
                impactedProjectStructure.Projects.Length projectStructure.Projects.Length
                impactedProjectStructure.Applications.Length projectStructure.Applications.Length
            {
                 Id = currentCommitId
                 Version = version
                 Base = Some snapshot
                 Branch = branch
                 ProjectStructure = projectStructure
                 ImpactedProjectStructure = impactedProjectStructure
                 Parameters = parameters
            }
        | None ->
            printfn "Last snapshot is not found. Full build should be done"
            commitIds |> String.join "," |> printfn "Checked commit ids : %s"
            {
                 Id = currentCommitId
                 Version = version
                 Base = None
                 Branch = branch
                 ProjectStructure = projectStructure
                 ImpactedProjectStructure = projectStructure
                 Parameters = parameters
            }

    let getFullBuild version parametersBuilder applications =
        let parameters = defaultParameters |> parametersBuilder
            
        let projectStructure = 
            parameters.Repository 
            |> Graph.readProjectStructure applications
        let commitIds = Git.getCommits projectStructure.RootFolder parameters.MaxCommitsCheck 
        let branch = projectStructure.RootFolder |> Git.getBranch
        let currentCommitId = commitIds |> List.head
        
        printfn "Ignoring snapshot. Full build was requested"
        printfn "Current commit id %s" currentCommitId
        printfn "Current branch %s" branch
        {
             Id = currentCommitId
             Version = version
             Base = None
             Branch = branch
             ProjectStructure = projectStructure
             ImpactedProjectStructure = projectStructure
             Parameters = parameters
        }
        
        
    let createView name folder projects =
        name
            |> sprintf "%s.sln"
            |> Pathes.combine folder
            |> Pathes.safeDelete 
         
        sprintf "dotnet new sln --name %s" name
            |> ProcessHelper.run folder
            |> ignore
        
        sprintf "dotnet sln %s.sln add %s" name (projects |> String.concat " ")
            |> ProcessHelper.run folder
            |> ignore
    
    let publish structure =
        structure.Applications
        |> Array.map (fun app -> 
            match app.Parameters with
            | DotnetApplication dotnetApp ->
                let project = structure.Projects |> Array.find (fun p -> p.Name = app.Name)
                async { return project |> dotnetApp.Publish }
            | CustomApplication customApp ->
                async { return () |> customApp.Publish }
        )
    
    let private deploy rootFolder deployments filterApps =
        let snapshotInfo =
            rootFolder
            |> FileStructure.getSnapshotDescriptionFilePath
            |> SnaphotDescription.Load
        let (impactedApplications:SnaphotDescription.App[]) =
            snapshotInfo |> filterApps
        
        impactedApplications
            |> Array.map (fun app ->
                let deployment = deployments |> Array.find (fun (dep:Application) -> dep.Name = app.App)
                let appInfo = { Name=app.App; Version=app.Version; SnapshotId=app.Snapshot}
                async { return [|appInfo|] |> deployment.Deploy } 
            )
            
    let deployImpacted rootFolder deployments =
        deploy rootFolder deployments (fun snapshotInfo -> snapshotInfo.Apps |> Array.filter (fun app -> app.Snapshot = snapshotInfo.Id))
        
    let deployAll rootFolder deployments =
        deploy rootFolder deployments (fun snapshotInfo -> snapshotInfo.Apps)
