namespace IncrementalBuild

open System
open System.IO
open Google.Cloud.Storage.V1

module GCSSnapshotStorage =
    
    let private client = StorageClient.Create();
    let private KB = 0x400;
    let private MB = 0x100000;

    let firstAvailableSnapshot bucket commitIds =
        let snapshots =
            client.ListObjects(bucket)
            |> Seq.map (fun x -> x.Name |> Path.GetFileNameWithoutExtension)
            |> Set.ofSeq
        commitIds |> Array.tryFind snapshots.Contains
    
    let saveSnapshot bucket zip =
        let zipName = zip |> Path.GetFileName
        use zipStream = File.Open(zip, FileMode.Open)
        client.UploadObject(bucket, zipName, "application/zip", zipStream, UploadObjectOptions(ChunkSize=Nullable<int>(10*MB)))
            |>ignore 
    
    let getSnapshot bucket snapshotId =
        let zipName = sprintf "%s.zip" snapshotId
        let tempFilePath = sprintf "%s/%s" (Path.GetTempPath()) zipName
        use outputFile = File.Create(tempFilePath)
        client.DownloadObject(bucket, zipName, outputFile, DownloadObjectOptions(ChunkSize=Nullable<int>(10*MB)))
        tempFilePath