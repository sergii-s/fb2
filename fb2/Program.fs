namespace IncrementalBuild

module String =
    open System
    let toLowerInvariant (s : string) =
        s.ToLowerInvariant()
    let split (splitWith : string) (s : string) =
        s.Split([|splitWith|], StringSplitOptions.RemoveEmptyEntries)
    let trim (trimChar : char) (s : string) =
        s.Trim(trimChar)


module Pathes =
    open System.IO
    type RelativePath = {
        Path : string
        RelativeFrom : string
    }
    type FilePath =
        | Absolute of string
        | Relative of RelativePath
    let safeDelete file = if file |> File.Exists then file |> File.Delete 
    let combine p1 p2 =
        Path.Combine(p1, p2)
    let toAbsolutePath relative =
        Path.Combine(relative.RelativeFrom.Replace('\\', '/'), relative.Path.Replace('\\', '/')) |> Path.GetFullPath


module ProcessHelper =
    open System.Diagnostics

    let run name args dir =
        let proc = new Process()
        proc.StartInfo.FileName <- name
        proc.StartInfo.Arguments <- args
        proc.StartInfo.WorkingDirectory <- dir
        proc.StartInfo.UseShellExecute <- false
        proc.StartInfo.RedirectStandardOutput <- true
        proc.StartInfo.RedirectStandardError <- true
        proc.Start() |> ignore
        proc.WaitForExit()
        proc.StandardOutput.ReadToEnd()
        

module Graph =
    open Pathes
    open FSharp.Data
    open System.IO
    
    type CsFsProject = XmlProvider<"csproj.xml", SampleIsList= true, EmbeddedResource="IncrementalBuild, csproj.xml">
    type OutputType = Exe | Lib
    type Project = {
        Name : string
        AssemblyName : string
        OutputType : OutputType
        ProjectPath : string
        ProjectFolder : string
        ProjectReferences : string array
        TargetFramework : string
    }

    type ProjectStructure = {
        Projects : Map<string, Project>
        RootFolder : string
    }

    let rec private scanProjectFiles dir = seq {
        yield! Directory.GetFiles(dir, "*.csproj") |> Array.map Path.GetFullPath
        yield! Directory.GetFiles(dir, "*.fsproj") |> Array.map Path.GetFullPath
        yield! 
            Directory.GetDirectories(dir) 
            |> Seq.collect scanProjectFiles
    }
    let private parseProjectFile (projectFile : string) = 
        try
            let project = projectFile |> CsFsProject.Load 
            let outputType = 
                let asString = 
                    project.PropertyGroups
                    |> Seq.choose (fun x -> x.OutputType)
                    |> Seq.tryHead
                    |> Option.map String.toLowerInvariant
                match asString with 
                | Some "exe" -> Exe
                | _ -> Lib
            let projectReferences = 
                project.ItemGroups 
                |> Seq.collect (fun x -> x.ProjectReferences)
                |> Seq.map (fun x -> { Path = x.Include; RelativeFrom = projectFile |> Path.GetDirectoryName})
                |> Seq.map toAbsolutePath
                |> Array.ofSeq
            let projectName = projectFile |> Path.GetFileNameWithoutExtension
            {   Name = projectName
                AssemblyName = project.PropertyGroups |> Array.tryPick (fun x -> x.AssemblyName) |> Option.defaultValue projectName
                OutputType = outputType
                ProjectPath = projectFile
                ProjectFolder = projectFile |> Path.GetDirectoryName
                ProjectReferences = projectReferences
                TargetFramework = ( project.PropertyGroups |> Array.head ).TargetFramework
            } |> Some
        with
        | e -> printfn "WARNING: failed to parse %s project file" projectFile; None

    let readProjectStructure dir =
        let projects = 
            dir
            |> scanProjectFiles 
            |> Seq.choose parseProjectFile
            |> Seq.map (fun p -> p.ProjectPath, p)
            |> Map.ofSeq
        let invalidProjects =
            projects
            |> Map.filter (fun _ project -> 
                                project.ProjectReferences 
                                |> Array.exists (fun dep -> projects |> Map.containsKey dep |> not) 
                           )
            
        invalidProjects
            |> Map.iter ( fun projectFile project -> printfn "WARNING: broken dependencies in %s project file. Ignoring project" projectFile)
            
        let validProjects = 
            projects
            |> Map.filter (fun projectFile project -> invalidProjects |> Map.containsKey projectFile |> not)
            
        {
            Projects = validProjects
            RootFolder = dir
        }
        
    let rec getReferencedProjects structure project = seq {
        yield! project.ProjectReferences |> Seq.map (fun p -> structure.Projects.[p])
        yield! project.ProjectReferences |> Seq.collect (fun p -> structure.Projects.[p] |> getReferencedProjects structure)
    }
    let rec getDependentProjects structure project = seq {
        let dependentProjects =
            structure.Projects
            |> Seq.filter (fun p -> p.Value.ProjectReferences |> Array.contains project.ProjectPath)
            |> Seq.map (fun p -> p.Value)
        yield! dependentProjects
        yield! dependentProjects |> Seq.collect (getDependentProjects structure)
    }
    let rec getProjectWithReferencedProjects structure project = seq {
        yield project   
        yield! getReferencedProjects structure project
    }
    let rec getProjectWithDependentProjects structure project = seq {
        yield project   
        yield! getDependentProjects structure project
    }
    let getImpactedProjects structure files =
        let filesFullPathes = 
            files 
            |> Seq.map (fun f -> Path.Combine(structure.RootFolder, f) |> Path.GetFullPath) 
            |> List.ofSeq
        let directImpactedProjects = seq {
            for p in structure.Projects do
                if filesFullPathes |> Seq.exists (fun f -> f.StartsWith((p.Key |> Path.GetDirectoryName) + Path.DirectorySeparatorChar.ToString())) then
                    yield p.Value
        }
        directImpactedProjects 
            |> Seq.collect (fun p -> p |> getProjectWithDependentProjects structure)
            |> Seq.distinct
            |> Array.ofSeq

module View =
    
    let create solution folder projects =
        solution
            |> sprintf "%s.sln"
            |> Pathes.combine folder
            |> Pathes.safeDelete 
         
        ProcessHelper.run "dotnet" (sprintf "new sln --name %s" solution) folder |> ignore
        ProcessHelper.run "dotnet" (sprintf "sln %s.sln add %s" solution (projects |> String.concat " ")) folder |> ignore

module Git =

    let getCommits repo count = 
        ProcessHelper.run "git" (sprintf "log -%i --pretty=format:'%%h'" count) repo
            |> String.split "\n"
            |> Array.map (String.trim '\'')

    let getDiffFiles repo commit1 commit2 =
        ProcessHelper.run "git" (sprintf "diff --name-only %s %s" commit1 commit2) repo
            |> String.split "\n"

module FileSystemSnapshotStorage =
    open System.IO
    
    let firstAvailableSnapshot path commitIds =
        let snapshots =
            Directory.EnumerateFiles(path, "*.zip")
            |> Seq.map Path.GetFileNameWithoutExtension
            |> Set.ofSeq
        commitIds |> Array.tryFind (fun commit -> snapshots.Contains commit)
        
module FB2 =

    open Graph
    
    type SourceControlProvider =
        | Git

    type SnaphotStorage =
        | FileSystem of string
        | GoogleCloudStorage
        
    type Builder = {
        Repository : string
        SourceControlProvider : SourceControlProvider
        SnaphotStorage : SnaphotStorage
        MaxCommitsCheck : int
    }
    
    let private defaultBuilder = {
        Repository = "."
        SourceControlProvider = SourceControlProvider.Git
        SnaphotStorage = SnaphotStorage.FileSystem "/tmp/snapshots"
        MaxCommitsCheck = 20
    }
    
    type IncrementalBuildInfo = {
        Id : string
        DiffId : string option
        ProjectStructure : ProjectStructure
        ImpactedProjects : Project array
        NotImpactedProjects : Project array
    }
    
    type BuildConfiguration =
        | Release
        | Debug
    
    let getAssemblies configuration projects =
        let conf =
            match configuration with
            | Release -> "Release"
            | Debug -> "Debug"
        projects
        |> Seq.map (fun p -> sprintf "%s/bin/%s/%s/%s.dll" p.ProjectFolder conf p.TargetFramework p.AssemblyName)
        
    let getAssembliesTest configuration projects =
        let conf =
            match configuration with
            | Release -> "Release"
            | Debug -> "Debug"
        projects
        |> Seq.collect (fun p -> [
            sprintf "%s/bin/%s/%s/*.*" p.ProjectFolder conf p.TargetFramework
            sprintf "%s/obj/%s/%s/*.*" p.ProjectFolder conf p.TargetFramework
        ])    
    
    let getIncrementalBuildStatus parametersFactory =
        let parameters = defaultBuilder |> parametersFactory
            
        let projectStructure = 
            parameters.Repository 
            |> Graph.readProjectStructure
        let commitIds = Git.getCommits projectStructure.RootFolder parameters.MaxCommitsCheck
        let currentCommitId = commitIds |> Array.head
        
        let lastSnapshotCommit =
            match parameters.SnaphotStorage with
            | FileSystem path ->  commitIds |> FileSystemSnapshotStorage.firstAvailableSnapshot path 
            | GoogleCloudStorage -> failwith "not implemented"
        
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
            }

