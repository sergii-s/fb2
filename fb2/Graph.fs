namespace IncrementalBuild
open System.IO

type OutputType = Exe | Lib
    
type DotnetProject = {
    Name : string
    AssemblyName : string
    OutputType : OutputType
    ProjectPath : string
    ProjectFolder : string
    ProjectReferences : string array
    TargetFramework : string
}
type CustomProject = {
    Name : string
    ProjectFolder : string
}
type Project =
    | DotnetProject
    | CustomProject

type ProjectStructure = {
    Projects : Map<string, Project>
    RootFolder : string
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
                |> Seq.map (fun x -> 
                    { Path = x.Include; RelativeFrom = projectFile |> Path.GetDirectoryName} 
                    |> toAbsolutePath
                )
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
            RootFolder = dir |> Path.GetFullPath
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
