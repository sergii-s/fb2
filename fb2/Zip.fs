namespace IncrementalBuild

module Zip =
    open System.IO
    open System.IO.Compression

    let zip rootFolder targetFile files = 
        use zipFile = new FileStream(targetFile, FileMode.Create)
        use archive = new ZipArchive(zipFile, ZipArchiveMode.Create)
        for file in files do
            let relativeFilePath = file |> Pathes.toRelativePath rootFolder
            archive.CreateEntryFromFile(file, relativeFilePath) |> ignore
        targetFile

    let unzip targetDirectory zipFileName =
        use zipFile = new FileStream(zipFileName, FileMode.Open)
        use archive = new ZipArchive(zipFile, ZipArchiveMode.Read)
        for file in archive.Entries do
            let completeFileName = Path.Combine(targetDirectory, file.FullName)
            let directory = Path.GetDirectoryName(completeFileName)
            if Directory.Exists(directory) |> not then
                Directory.CreateDirectory(directory) |> ignore
            if file.Name <> "" then
                file.ExtractToFile(completeFileName, true)
        