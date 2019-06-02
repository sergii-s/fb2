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
    
    type Artifact = {
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
    
    type Application = {
        Name : string
        DependsOn : string array
        Parameters : ApplicationType
        Deploy : Artifact array -> unit
    }
    
    type ProjectStructure = {
        Applications : Application array
        Projects : Project array
        RootFolder : string
    }
    
