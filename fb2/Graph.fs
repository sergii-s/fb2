namespace IncrementalBuild
open System.IO


type OutputType = Exe | Lib
    
type Project = {
    Name : string
    AssemblyName : string
    OutputType : OutputType
    ProjectPath : string
    ProjectFolder : string
    DependsOnFolders : string array
    ProjectReferences : string array
    TargetFramework : string
}

type Artifact = {
    Name : string
    Version : string
    SnapshotId : string
}

type DotnetApplication  = {
    Publish : Project -> int
}

type CustomApplication  = {
    RootFolder : string
    Publish : string -> int
}

type ApplicationType =
    | DotnetApplication of DotnetApplication
    | CustomApplication of CustomApplication

type Application = {
    Name : string
    DependsOn : string array
    Parameters : ApplicationType
    Deploy : Artifact array -> int
}

type ProjectStructure = {
    Applications : Application array
    Projects : Project array
    RootFolder : string
}



module Application =
    let NoDeployment = fun _ -> 0
    let NoPublish = fun _ -> 0
    
    type DotnetApplicationProperties = {
        DependsOn : string array
        Publish : Project -> int
        Deploy : Artifact[] -> int
    }
    type CustomApplicationProperties = {
        DependsOn : string array
        Publish : string -> int
        Deploy : Artifact[] -> int
    }
    let private defaultParamsDotnetApp = {
        DotnetApplicationProperties.DependsOn = [||]
        Publish = NoPublish
        Deploy = NoDeployment
    }
    let private defaultParamsCustomApp = {
        CustomApplicationProperties.DependsOn = [||]
        Publish = NoPublish
        Deploy = NoDeployment
    }

    let dotnet name (parameters:DotnetApplicationProperties) =
        {
            Name = name
            DependsOn = parameters.DependsOn
            Parameters = DotnetApplication {DotnetApplication.Publish = parameters.Publish }
            Deploy = parameters.Deploy
        }
    let custom name folder (parameters:CustomApplicationProperties) =
        {
            Name = name
            DependsOn = parameters.DependsOn
            Parameters = CustomApplication {CustomApplication.RootFolder=folder; Publish = parameters.Publish }
            Deploy = parameters.Deploy
        }        

module Graph =
    open Pathes
    open FSharp.Data
    
    type CsFsProject = XmlProvider<"csproj.xml", SampleIsList= true, EmbeddedResource="IncrementalBuild, csproj.xml">
    
    let rec private scanProjectFiles dir = seq {
        yield! Directory.GetFiles(dir, "*.csproj") |> Array.map Path.GetFullPath
        yield! Directory.GetFiles(dir, "*.fsproj") |> Array.map Path.GetFullPath
        yield! 
            Directory.GetDirectories(dir) 
            |> Seq.collect scanProjectFiles
    }
    let private parseProjectFile (apps:Map<string, Application>) (projectFile : string) = 
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
                |> Seq.map (fun x -> 
                    { Path = x.Include; RelativeFrom = projectFile |> Path.GetDirectoryName} 
                    |> toAbsolutePath
                )
                |> Array.ofSeq
            
            let projectName = projectFile |> Path.GetFileNameWithoutExtension
            let app = apps.TryFind projectName
            let customReferences = app |> Option.map(fun app->app.DependsOn) |> Option.defaultValue [||]
            {   Name = projectName
                AssemblyName = project.PropertyGroups |> Array.tryPick (fun x -> x.AssemblyName) |> Option.defaultValue projectName
                OutputType = outputType
                ProjectPath = projectFile
                ProjectFolder = projectFile |> Path.GetDirectoryName
                DependsOnFolders = customReferences
                ProjectReferences = projectReferences
                TargetFramework = ( project.PropertyGroups |> Array.head ).TargetFramework
            } |> Some
        with
        | e -> printfn "WARNING: failed to parse %s project file" projectFile; None

    let readProjectStructure (apps:Application array) dir =
        let appsByName =
            apps
            |> Seq.ofArray
            |> Seq.map(fun app -> app.Name, app)
            |> Map.ofSeq
            
        let dotnetProjects = 
            dir
            |> scanProjectFiles 
            |> Seq.choose (parseProjectFile appsByName)
            |> Seq.map (fun p -> p.ProjectPath, p)
            |> Map.ofSeq
            
        let invalidProjects =
            dotnetProjects
            |> Map.filter (fun _ project -> 
                                project.ProjectReferences 
                                |> Array.exists (fun dep -> dotnetProjects |> Map.containsKey dep |> not) 
                           )
            
        invalidProjects
            |> Map.iter ( fun projectFile project -> printfn "WARNING: broken dependencies in %s project file. Ignoring project" projectFile)
        
        let validProjects = 
            dotnetProjects
            |> Map.filter (fun projectFile project -> invalidProjects |> Map.containsKey projectFile |> not)
            |> Map.toSeq
            |> Seq.map snd
            |> Array.ofSeq
        
        apps |> Array.iter (fun app ->
            match app.Parameters with
            | DotnetApplication dotnetProjectApplication ->
                if validProjects |> Array.exists(fun project -> project.Name = app.Name) |> not then
                    failwithf "Applicaion %s not found in project structure" app.Name
            | CustomApplication customApplication ->
                if customApplication.RootFolder |> Pathes.combine dir |> Directory.Exists |> not then
                    failwithf "Applicaion %s not found in the repository folder" customApplication.RootFolder
        )
        
        {
            Applications = apps
            Projects = validProjects
            RootFolder = dir |> Path.GetFullPath
        }
        
    let rec getReferencedProjects (projectMap:Map<string, Project>) project = seq {
        yield! project.ProjectReferences |> Seq.map (fun p -> projectMap.[p])
        yield! project.ProjectReferences |> Seq.collect (fun p -> projectMap.[p] |> getReferencedProjects projectMap)
    }
    let rec getDependentProjects structure project = seq {
        let dependentProjects =
            structure.Projects
            |> Seq.filter (fun p -> p.ProjectReferences |> Array.contains project.ProjectPath)
        yield! dependentProjects
        yield! dependentProjects |> Seq.collect (getDependentProjects structure)
    }
    let rec getProjectWithReferencedProjects structure project =
        let projectMap = structure.Projects |> Array.map (fun p -> p.ProjectPath, p) |> Map.ofSeq
        seq {
            yield project   
            yield! getReferencedProjects projectMap project
        }
    let rec getProjectWithDependentProjects structure project =  
        seq {
            yield project   
            yield! getDependentProjects structure project
        }
            
    let getImpactedProjects structure files =
        let isProjectsApplication app (project:Project) =
            match app.Parameters with
            | DotnetApplication _ -> project.Name = app.Name
            | _ -> false
        let directorySeparatorString = Path.DirectorySeparatorChar.ToString()
        let filesFullPathes = 
            files 
            |> Seq.map (fun f -> Path.Combine(structure.RootFolder, f) |> Path.GetFullPath) 
            |> List.ofSeq
        let directImpactedProjects =
            seq {
                for p in structure.Projects do
                    let directImpactFolders = 
                        p.ProjectFolder + directorySeparatorString
                        ::
                        (p.DependsOnFolders |> Array.map (fun d -> (d |> Path.GetDirectoryName) + directorySeparatorString) |> List.ofArray)
                    let isImpacted =
                        filesFullPathes |> Seq.exists (fun f -> directImpactFolders |> List.exists(fun d -> d |> f.StartsWith))
                    if isImpacted then
                        yield p
            }
            |> Array.ofSeq
        let directImpactedApplications = seq {
            for a in structure.Applications do
                let directImpactFolders =
                    seq {
                        yield! a.DependsOn |> Array.map (fun d -> (d |> Path.GetDirectoryName) + directorySeparatorString)
                        match a.Parameters with
                        | CustomApplication app -> yield (app.RootFolder |> Path.GetDirectoryName) + directorySeparatorString
                        | _ -> ()
                    } |> List.ofSeq
                
                let isImpacted =
                    filesFullPathes |> Seq.exists (fun f -> directImpactFolders |> List.exists(fun d -> d |> f.StartsWith))
                if isImpacted then
                    yield a
        }
        let allImpactedProjects =
            directImpactedProjects 
            |> Seq.collect (fun p -> p |> getProjectWithDependentProjects structure)
            |> Seq.distinct
            |> Array.ofSeq
        let allImpactedApplications =
            seq {
                yield! directImpactedApplications
                yield! structure.Applications |> Array.filter (fun app -> allImpactedProjects |> Array.exists (isProjectsApplication app))
            }
            |> Seq.distinctBy(fun app -> app.Name)
            |> Array.ofSeq    
        {
            Applications = allImpactedApplications
            Projects = allImpactedProjects
            RootFolder = structure.RootFolder
        }
