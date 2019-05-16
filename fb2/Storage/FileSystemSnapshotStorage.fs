namespace IncrementalBuild

open System.IO

module FileSystemSnapshotStorage =
    open Model
    
    let firstAvailableSnapshot storagePath branches commitIds =
        let snapshots =
            branches
            |> List.collect
                   (
                        fun branch ->
                            Directory.EnumerateFiles(Path.Combine(storagePath, branch), "*.zip")
                            |> Seq.map (fun f -> f |> Path.GetFileNameWithoutExtension, {Id = f |> Path.GetFileNameWithoutExtension; Branch = branch})
                            |> List.ofSeq
                    )
            |> Map.ofSeq
        commitIds |> List.tryPick snapshots.TryFind
        
    let saveSnapshot storagePath (snapshot:Snapshot) zip =
        File.Move(zip, sprintf "%s/%s/%s.zip" storagePath snapshot.Branch snapshot.Id)    

    let getSnapshot storagePath (snapshot:Snapshot) =
        Path.Combine(storagePath, snapshot.Branch, sprintf "%s.zip" snapshot.Id) 