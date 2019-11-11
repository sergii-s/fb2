namespace IncrementalBuild
open FSharp.Data
open System.IO
open Model

type SnapshotDescription = JsonProvider<""" { 
    "id" : "asdlkfds",
    "artifacts" : [{ "artifact":"bidder", "snapshot":"sjg8sjen", "version":"1.0.0" }],
    "deployments" : [{ "deployment":"bidder-eu", "snapshot":"sjg8sjen", "version":"1.0.0" }]
}""">    

module Snapshots =

    let private createSnapshot id version projectStructure impactedProjectStructure =
        //todo to refactor this shit
        let a1 = projectStructure.Artifacts |> Array.map Artifact.getName
        let a2 = impactedProjectStructure.Artifacts |> Array.map Artifact.getName
        let d1 = projectStructure.Deployments |> Array.map Deployment.getName
        let d2 = impactedProjectStructure.Deployments |> Array.map Deployment.getName
        if a1 <> a2 || d1 <> d2 then
            failwith "Oh damn! You should rebuild all"

        let artifactsSnapshots = 
            projectStructure.Artifacts
            |> Array.map (fun (artifact:Artifact) -> 
                SnapshotDescription.Artifact(artifact.Name, id, version)
            )
        let deploymentSnapshots = 
            projectStructure.Deployments
            |> Array.map (fun (deployment:Deployment) -> 
                SnapshotDescription.Deployment(deployment.Name, id, version)
            )
        SnapshotDescription.Root(id, artifactsSnapshots, deploymentSnapshots) 
    
    let private updateSnapshot (oldSnapshot:SnapshotDescription.Root) id version projectStructure impactedProjectStructure =
        let artifactSnapshots = 
            projectStructure.Artifacts
            |> Array.map (fun artifact -> 
                match impactedProjectStructure.Artifacts |> Array.tryFind (Artifact.withName artifact.Name) with
                | Some artifact -> SnapshotDescription.Artifact(artifact.Name, id, version)
                | None -> oldSnapshot.Artifacts |> Array.find (fun artifact' -> artifact'.Artifact = artifact.Name)
            )
        let deploymentSnapshots = 
            projectStructure.Deployments
            |> Array.map (fun deployment -> 
                match impactedProjectStructure.Deployments |> Array.tryFind (Deployment.withName deployment.Name) with
                | Some deployment -> SnapshotDescription.Deployment(deployment.Name, id, version)
                | None -> oldSnapshot.Deployments |> Array.find (fun deployment' -> deployment'.Deployment = deployment.Name)
            )
        SnapshotDescription.Root(id, artifactSnapshots, deploymentSnapshots)       

    let readSnapshotFile (filePath:string) = 
        filePath 
        |> SnapshotDescription.Load
    
    let updateSnapshotFile filePath id version p1 p2 = 
        let saveSnapshot (snapshot:SnapshotDescription.Root) =     
            use writer = filePath |> File.CreateText 
            snapshot.JsonValue.WriteTo(writer, JsonSaveOptions.None)
    
        let getOrCreate = 
            if filePath |> File.Exists then
                filePath
                |> readSnapshotFile       
                |> updateSnapshot
            else
                createSnapshot
        
        getOrCreate id version p1 p2 
        |> saveSnapshot
    
    let asArtifactSnapshot (artifact:SnapshotDescription.Artifact) =
        {
            ArtifactSnapshot.Name = artifact.Artifact
            Version = artifact.Version
            SnapshotId = artifact.Snapshot
        }