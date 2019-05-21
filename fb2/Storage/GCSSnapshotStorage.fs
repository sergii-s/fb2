namespace IncrementalBuild

open System
open System.IO
open Google.Cloud.Storage.V1

module GCSSnapshotStorage =
    open Model
    
    let private client = StorageClient.Create();
    let private KB = 0x400;
    let private MB = 0x100000;

    let firstAvailableSnapshot bucket branches commitIds =
        let snapshots =
            branches
            |> List.collect
                   (fun branch ->
                                client.ListObjects(bucket, sprintf "%s/" branch)
                                |> Seq.map (fun p ->
                                    p.Name |> Path.GetFileNameWithoutExtension, {Id = p.Name |> Path.GetFileNameWithoutExtension;Branch = branch }
                                )
                                |> List.ofSeq
                    )
            |> Map.ofList
        commitIds |> List.tryPick snapshots.TryFind
    
    let saveSnapshot bucket (snapshot:Snapshot) zip =
        let zipName = sprintf "%s/%s.zip" snapshot.Branch snapshot.Id
        use zipStream = File.Open(zip, FileMode.Open)
        printfn "Uploading snapshot %s" zipName
        client.UploadObject(bucket, zipName, "application/zip", zipStream, UploadObjectOptions(ChunkSize=Nullable<int>(10*MB)))
            |>ignore 
    
    let getSnapshot bucket (snapshot:Snapshot) =
        let zipName = sprintf "%s/%s.zip" snapshot.Branch snapshot.Id
        let tempFilePath = sprintf "%s/%s.zip" (Path.GetTempPath()) snapshot.Id
        use outputFile = File.Create(tempFilePath)
        client.DownloadObject(bucket, zipName, outputFile, DownloadObjectOptions(ChunkSize=Nullable<int>(10*MB)))
        tempFilePath