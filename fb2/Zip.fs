namespace IncrementalBuild

module Zip =
    open System.IO
    open System.IO.Compression

    let zip rootFolder targetFile files = 
        use zipFile = new FileStream(targetFile, FileMode.Create)
        use archive = new ZipArchive(zipFile, ZipArchiveMode.Update)
        for file in files do
            let relativeFilePath = file |> Pathes.toRelativePath rootFolder
            archive.CreateEntryFromFile(file, relativeFilePath) |> ignore
        targetFile

    let unzip targetDirectory zipFile =
        ZipFile.ExtractToDirectory(zipFile, targetDirectory)
