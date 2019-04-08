namespace IncrementalBuild

open System.IO

module FileSystemSnapshotStorage =
    
    let firstAvailableSnapshot storagePath commitIds =
        let snapshots =
            Directory.EnumerateFiles(storagePath, "*.zip")
            |> Seq.map Path.GetFileNameWithoutExtension
            |> Set.ofSeq
        commitIds |> Array.tryFind snapshots.Contains
        
    let saveSnapshot storagePath zip =
        let zipName = zip |> Path.GetFileName
        File.Move(zip, sprintf "%s/%s" storagePath zipName)    

    let getSnapshot storagePath snapshotId =
        Path.Combine(storagePath, sprintf "%s.zip" snapshotId) 