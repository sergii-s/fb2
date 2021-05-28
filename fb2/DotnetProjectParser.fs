namespace IncrementalBuild

module DotnetProjectParser =
    open FSharp.Data
    open IncrementalBuild.Model
    open Pathes
    open System.IO

    type CsFsProject = XmlProvider<"csproj.xml", SampleIsList= true, EmbeddedResource="IncrementalBuild, csproj.xml">

    let private scanProjectFiles dir = seq {
        yield! Directory.GetFiles(dir, "*.csproj", SearchOption.AllDirectories) |> Array.map Path.GetFullPath
        yield! Directory.GetFiles(dir, "*.fsproj", SearchOption.AllDirectories) |> Array.map Path.GetFullPath
    }

    let parseProjectFile rootFolder (projectFile : string) =
        try
            let project = projectFile |> CsFsProject.Load

            let projectReferences =
                project.ItemGroups
                |> Seq.collect (fun x -> x.ProjectReferences)
                |> Seq.map (fun x ->
                    { Path = x.Include; RelativeFrom = projectFile |> Path.GetDirectoryName}
                    |> toAbsolutePath
                    |> toRelativePath rootFolder
                )
                |> Array.ofSeq

            let externalReferences =
                project.ItemGroups
                |> Seq.collect (fun group -> group.Contents)
                |> Seq.map (fun x ->
                    { Path = x.Include; RelativeFrom = projectFile |> Path.GetDirectoryName }
                    |> toAbsolutePath
                    |> toRelativePath rootFolder
                )
                |> Array.ofSeq

            let projectName = projectFile |> Path.GetFileNameWithoutExtension
            let assemblyName = project.PropertyGroups |> Array.tryPick (fun x -> x.AssemblyName) |> Option.defaultValue projectName
            let framework = ( project.PropertyGroups |> Array.head ).TargetFramework
            let projectFile = projectFile |> toRelativePath rootFolder
            let isPublishable = project.PropertyGroups |> Array.tryPick (fun x -> x.IsPublishable) |> Option.defaultValue true
            Project.Create projectName assemblyName projectFile projectReferences externalReferences framework isPublishable
                |> Some
        with
        | e -> printfn "WARNING: failed to parse %s project file. %A" projectFile e; None

    let parseDotnetProjects dir =
        dir
        |> scanProjectFiles
        |> Seq.choose (parseProjectFile dir)
