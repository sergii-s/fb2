module String =
    open System
    let toLowerInvariant (s : string) =
        s.ToLowerInvariant()
    let split (splitWith : string) (s : string) =
        s.Split(splitWith, StringSplitOptions.RemoveEmptyEntries)
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
        

module FB2 =
    open Pathes
    open FSharp.Data
    open System.IO
    
    type CsFsProject = XmlProvider<"csproj.xml", SampleIsList= true>
    type OutputType = Exe | Lib
    type Project = {
        Name : string
        OutputType : OutputType
        ProjectPath : string
        ProjectReferences : string array
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
            {   Name = projectFile |> Path.GetFileNameWithoutExtension
                OutputType = outputType
                ProjectPath = projectFile
                ProjectReferences = projectReferences
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
        let validProjects = 
            projects
            |> Map.filter (fun _ project -> 
                                project.ProjectReferences 
                                |> Array.forall (fun dep -> projects |> Map.containsKey dep) 
                           )
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


module Git =

    let getCommits repo count = 
        ProcessHelper.run "git" (sprintf "log -%i --pretty=format:'%%h'" count) repo
            |> String.split "\n"
            |> Array.map (String.trim '\'')

    let getDiffFiles repo commit1 commit2 =
        ProcessHelper.run "git" (sprintf "diff --name-only %s %s" commit1 commit2) repo
            |> String.split "\n"

        
[<EntryPoint>]
let main argv =
    printfn "Hello World from F#!"
    let projectStructure = 
        "/home/sergii/dev/antvoice" 
        |> FB2.readProjectStructure
    let getLastCommits = Git.getCommits projectStructure.RootFolder 2 
    let modifiedFiles = Git.getDiffFiles projectStructure.RootFolder (getLastCommits |> Array.head) (getLastCommits |> Array.last)
    let impactedProjects = modifiedFiles |> FB2.getImpactedProjects projectStructure |> Array.ofSeq
    0 // return an integer exit code
