namespace fb2.UnitTests
open System
open IncrementalBuild
open Xunit
open Model

module Tests =
    open System
    open System.IO
    open NFluent

    [<Fact>]
    let ``Should properly read project structure`` () =
        let repoRelativePath = "../../../../"
        let repoAbsolutePath = Path.Combine(Directory.GetCurrentDirectory(), repoRelativePath) |> Path.GetFullPath
        let structure = Graph.readProjectStructure [||] repoRelativePath
        let project name = structure.Projects |> Array.find (fun p -> p.Name = name)
        
        let folders = 
            structure.Projects 
            |> Array.map (fun p -> p.ProjectFolder)
            |> Array.sort 
        
        Check.That(structure.RootFolder).IsEqualTo(repoAbsolutePath) |> ignore            
        Check.That(seq folders).ContainsExactly("/", "fb2/", "tests/") |> ignore
        Check.That(seq (project "tests").ProjectReferences).ContainsExactly("fb2/fb2.fsproj") |> ignore
        
    [<Fact>]
    let ``Impacted projects detection`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [|
                Project.Create "Project1" "Project1Assembly" OutputType.Lib "project1/project1.csproj" [||] "standard2.0"
            |]
            Artifacts = [||]
            Deployments = [||]
        }
        let files = [|"project1/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).Not.IsEmpty() |> ignore
        
    [<Fact>]
    let ``Impacted projects - by project dependency`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [|
                Project.Create "Project1" "Project1Assembly" OutputType.Lib "project1/project1.csproj" [||] "standard2.0"
                Project.Create "Project2" "Project1Assembly" OutputType.Lib "project2/project2.csproj" [|"project1/project1.csproj"|] "standard2.0"
            |]
            Artifacts = [||]
            Deployments = [||]
        }
        let files = [|"project1/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(seq impacted.Projects).HasSize(int64 2) |> ignore
    
    [<Fact>]
    let ``Impacted applications detection - project trigger`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [|
                Project.Create "Project1" "Project1Assembly" OutputType.Lib "project1/project1.csproj" [||] "standard2.0"
            |]
            Artifacts = [|
                {
                    Artifact.Name = "Project1"
                    DependsOn = [||]
                    Parameters = { DotnetApplication.Publish = ignore } |> DotnetApplication
                }
            |]
            Deployments = [||]
        }
        let files = [|"project1/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).Not.IsEmpty() |> ignore
        Check.That(impacted.Artifacts).Not.IsEmpty() |> ignore

    [<Fact>]
    let ``Impacted applications detection - depends on trigger`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [|
                Project.Create "Project1" "Project1Assembly" OutputType.Lib "project1/project1.csproj" [||] "standard2.0"
            |]
            Artifacts = [|
                {
                    Artifact.Name = "Project1"
                    DependsOn = [|"folder1"|]
                    Parameters = { DotnetApplication.Publish = ignore } |> DotnetApplication
                }
            |]
            Deployments = [||]
        }
        let files = [|"folder1/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).Not.IsEmpty() |> ignore
        Check.That(impacted.Artifacts).Not.IsEmpty() |> ignore   
        
    [<Fact>]
    let ``Impacted applications detection - custom app - base folder trigger`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [||]
            Artifacts = [|
                {
                    Artifact.Name = "Project1"
                    DependsOn = [|"somefolder"|]
                    Parameters = { CustomApplication.Publish = ignore } |> CustomApplication
                }
            |]
            Deployments = [||]
        }
        let files = [|"somefolder/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).IsEmpty() |> ignore
        Check.That(impacted.Artifacts).Not.IsEmpty() |> ignore   

    [<Fact>]
    let ``Impacted deployments detection - folder dependency`` () =
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [||]
            Artifacts = [|
                {
                    Artifact.Name = "Project1"
                    DependsOn = [|"somefolder"|]
                    Parameters = { CustomApplication.Publish = ignore } |> CustomApplication
                }
            |]
            Deployments = [|
                {
                    Deployment.Name = "Deployment1"
                    DependsOnFolders = [|"folder2"|]
                    DependsOnArtifacts = [||]
                    Deploy = ignore
                }
            |]
        }
        let files = [|"folder2/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).IsEmpty() |> ignore
        Check.That(impacted.Artifacts).IsEmpty() |> ignore   
        Check.That(impacted.Deployments).Not.IsEmpty() |> ignore   

        
    [<Fact>]
    let ``Impacted deployments detection - artifact dependency`` () =
        let artifact = {
            Artifact.Name = "Project1"
            DependsOn = [|"folder1"|]
            Parameters = { CustomApplication.Publish = ignore } |> CustomApplication
        }
        let structure = {
            ProjectStructure.RootFolder = "/somefolder"
            Projects = [||]
            Artifacts = [|
                artifact    
            |]
            Deployments = [|
                {
                    Deployment.Name = "Deployment1"
                    DependsOnFolders = [|"folder2"|]
                    DependsOnArtifacts = [| artifact |]
                    Deploy = ignore
                }
            |]
        }
        let files = [|"folder1/file1.txt"|]
        let impacted = Graph.getImpactedProjects structure files
        Check.That(impacted.Projects).IsEmpty() |> ignore
        Check.That(impacted.Artifacts).Not.IsEmpty() |> ignore   
        Check.That(impacted.Deployments).Not.IsEmpty() |> ignore   

    

    [<Fact>]
    let ``Semanthic usage`` () =
//        let artifacts = {
//            bidder = Application.dotnet
//        }
//        
        let artifacts = {| Label1 = "string" |}
        ()