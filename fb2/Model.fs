namespace IncrementalBuild
open System.IO

module Model =
    type Snapshot = {
        Id : string
        Branch : string
    }
    
    type OutputType = Exe | Lib
//    type FolderPath = private FolderPath of string
//    module FolderPath = 
//        let create path = 
//            
            
    type Project = {
        Name : string
        AssemblyName : string
        OutputType : OutputType
        ProjectPath : string
        ProjectFolder : string
        ProjectReferences : string array
        TargetFramework : string
    } with 
        static member Create name assemblyName output projectPath references framework =
            {
                Project.Name = name
                AssemblyName = assemblyName
                OutputType = output
                ProjectPath = projectPath
                ProjectFolder = projectPath |> Path.GetDirectoryName |> Pathes.ensureDirSeparator
                ProjectReferences = references
                TargetFramework = framework
            }
    
    type ArtifactSnapshot = {
        Name : string
        Version : string
        SnapshotId : string
    }
    
    type DotnetApplication  = {
        Publish : Project -> unit
    }
    
    type CustomApplication  = {
        Publish : unit -> unit
    }
    
    type ApplicationType =
        | DotnetApplication of DotnetApplication
        | CustomApplication of CustomApplication
    
    type Artifact = {
        Name : string
        DependsOn : string array
        Parameters : ApplicationType
        Deploy : ArtifactSnapshot array -> unit
    }
    
    type Deployment = {
        Name : string
        DependsOnFolders : string array
        DependsOnArtifacts : Artifact array
        Deploy : ArtifactSnapshot array -> unit
    }

    type ProjectStructure = {
        Artifacts : Artifact array
        Deployments : Deployment array
        Projects : Project array
        RootFolder : string
    }
    
    module Project = 
        let getName (project:Project) = project.Name
        let withName name = getName >> (=) name
        
    module Artifact = 
        let getName (artifact:Artifact) = artifact.Name
        let withName name = getName >> (=) name