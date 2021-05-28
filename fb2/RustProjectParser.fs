namespace IncrementalBuild

open System
open System.IO
open IncrementalBuild.Model
open Tommy
open Pathes

module RustProjectParser =

    let private scanProjectFiles dir =
        seq {
            yield!
                Directory.GetFiles(dir, "Cargo.toml", SearchOption.AllDirectories)
                |> Array.map Path.GetFullPath
        }

    let parseProjectFile rootFolder (projectFile: string) =
        try
            use projectFileContent = new StreamReader(projectFile)
            let toml = TOML.Parse(projectFileContent)

            let name =
                toml.Item("package").Item("name").ToString()

            let framework =
                toml.Item("package").Item("edition").ToString()

            let getPath (dependency: TomlNode) =
                if dependency.IsString
                   || String.IsNullOrWhiteSpace(dependency.Item("path").ToString())
                   || dependency.Item("path").ToString() = "Tommy.TomlLazy" then
                    None
                else
                    Some(dependency.Item("path").ToString())

            let dependencies =
                toml.Item("dependencies").Children
                |> Seq.choose getPath
                |> Seq.map (fun p ->
                    let project = { Path = p; RelativeFrom = projectFile |> Path.GetDirectoryName }
                                  |> toAbsolutePath
                                  |> toRelativePath rootFolder
                    project + "/Cargo.toml"
                )
                |> Array.ofSeq

            let projectFile = projectFile |> toRelativePath rootFolder

            Project.Create name name projectFile dependencies Array.empty framework true
            |> Some
        with e ->
            printfn "WARNING: failed to parse %s project file. %A" projectFile e
            None

    let parseRustProjects rootFolder =
        rootFolder
        |> scanProjectFiles
        |> Seq.choose (parseProjectFile rootFolder)
