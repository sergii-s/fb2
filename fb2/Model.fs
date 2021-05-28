namespace IncrementalBuild
open System.IO

module Model =
    type Snapshot = {
        Id : string
        Branch : string
    }

    type Project = {
        Name : string
        AssemblyName : string
        ProjectPath : string
        ProjectFolder : string
        ProjectReferences : string array
        ExternalReferences : string array
        TargetFramework : string
        IsPublishable : bool
    } with
        static member Create name assemblyName projectPath references externalReferences framework isPublishable =
            {
                Project.Name = name
                AssemblyName = assemblyName
                ProjectPath = projectPath
                ProjectFolder = projectPath |> Path.GetDirectoryName |> Pathes.ensureDirSeparator
                ProjectReferences = references
                ExternalReferences = externalReferences
                TargetFramework = framework
                IsPublishable = isPublishable
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

    type BuildInfo = {
        Structure : ProjectStructure
        CommitIds : string list
        CurrentCommitId : string
        Branch : string
    }


