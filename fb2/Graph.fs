namespace IncrementalBuild
open System.IO
open Model

module Application =

    type DotnetApplicationProperties = {
        DependsOn : string array
        Publish : Project -> unit
        Deploy : Artifact[] -> unit
    }
    type CustomApplicationProperties = {
        DependsOn : string array
        Publish : unit -> unit
        Deploy : Artifact[] -> unit
    }
    let dotnet name (parameters:DotnetApplicationProperties) =
        {
            Name = name
            DependsOn = parameters.DependsOn
            Parameters = DotnetApplication {DotnetApplication.Publish = parameters.Publish }
            Deploy = parameters.Deploy
        }
    let custom name (parameters:CustomApplicationProperties) =
        if parameters.DependsOn |> Array.isEmpty then failwithf "Custom application should have at least one folder dependency for app %s" name
        {
            Name = name
            DependsOn = parameters.DependsOn
            Parameters = CustomApplication { Publish = parameters.Publish }
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
    let private parseProjectFile rootFolder (projectFile : string) =
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
                    |> Pathes.toAbsolutePath
                    |> Pathes.toRelativePath rootFolder
                )
                |> Array.ofSeq

            let externalReferences =
                project.ItemGroups
                |> Seq.choose (fun group -> group.Content)
                |> Seq.map (fun x ->
                    { Path = x.Include; RelativeFrom = projectFile |> Path.GetDirectoryName}
                    |> Pathes.toAbsolutePath
                    |> Pathes.toRelativePath rootFolder
                )
                |> Array.ofSeq

            let projectName = projectFile |> Path.GetFileNameWithoutExtension
            let assemblyName = project.PropertyGroups |> Array.tryPick (fun x -> x.AssemblyName) |> Option.defaultValue projectName
            let framework = ( project.PropertyGroups |> Array.head ).TargetFramework
            let projectFile = projectFile |> Pathes.toRelativePath rootFolder
            let isPublishable = project.PropertyGroups |> Array.tryPick (fun x -> x.IsPublishable) |> Option.defaultValue true
            Project.Create projectName assemblyName outputType projectFile projectReferences externalReferences framework isPublishable
                |> Some
        with
        | e -> printfn "WARNING: failed to parse %s project file. %A" projectFile e; None

    let readProjectStructure (apps:Application array) dir =
        let dotnetProjects =
            dir
            |> scanProjectFiles
            |> Seq.choose (parseProjectFile dir)
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

        let notPublishableProjects =
            dotnetProjects
            |> Map.filter (fun _ project ->
                apps
                    |> Array.exists (fun app ->
                                      match app.Parameters with
                                      | DotnetApplication _ -> app.Name = project.Name
                                      | _ -> false )
                    |> not
            )

        notPublishableProjects
            |> Map.iter (fun projectFile project -> if project.IsPublishable then printfn "WARNING: not publishable project %s will be published. Add <IsPublishable>false</IsPublishable> property" projectFile else ())

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
                for dependsDir in app.DependsOn do
                    if dependsDir |> Pathes.combine dir |> Directory.Exists |> not then
                        failwithf "Applicaion %s not found in the repository folder" app.Name
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

    let getImpactedProjects structure (files:string array) =
        let getCorrespondingApplication (project:Project) =
            structure.Applications |> Array.tryFind (fun app -> app.Name = project.Name)

        let getCorrespondingDotnetProject (app:Application) =
            match app.Parameters with
            | DotnetApplication _ ->
                structure.Projects
                    |> Array.find (fun p -> p.Name = app.Name)
                    |> Some
            | _ ->
                None

        let isProjectImpacted project =
            files
            |> Seq.exists(fun f -> project.ProjectFolder |> f.StartsWith ||
                                   project.ExternalReferences
                                   |> Seq.exists(fun extRef -> extRef |> f.StartsWith))

        let directImpactedProjects =
            structure.Projects
            |> Array.where isProjectImpacted

        let directImpactedApplications =
            structure.Applications
                |> Array.where (
                    fun app -> app.DependsOn
                                |> Array.map Pathes.ensureDirSeparator
                                |> Array.exists(fun dependsOnDir -> files |> Seq.exists (fun f -> dependsOnDir |> f.StartsWith))
                )

        let allImpactedProjects =
            seq {
                yield! directImpactedProjects |> Seq.collect (fun p -> p |> getProjectWithDependentProjects structure)
                yield! directImpactedApplications |> Array.choose getCorrespondingDotnetProject
            }
            |> Seq.distinct
            |> Array.ofSeq

        let allImpactedApplications =
            seq {
                yield! directImpactedApplications
                yield! allImpactedProjects |> Array.choose getCorrespondingApplication
            }
            |> Seq.distinctBy(fun app -> app.Name)
            |> Array.ofSeq
        {
            Applications = allImpactedApplications
            Projects = allImpactedProjects
            RootFolder = structure.RootFolder
        }
