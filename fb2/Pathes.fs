namespace IncrementalBuild
open System
open System.IO

module Pathes =
    type RelativePath = {
        Path : string
        RelativeFrom : string
    }
    type FilePath =
        | Absolute of string
        | Relative of RelativePath
    let safeDelete file = if file |> File.Exists then file |> File.Delete 
    let combine p1 p2 =
        Path.Combine(p1, p2)
    let toAbsolutePath relative =
        Path.Combine(relative.RelativeFrom.Replace('\\', '/'), relative.Path.Replace('\\', '/')) |> Path.GetFullPath
    let ensureDirSeparator (dir:string) = 
        if dir.EndsWith("/") then 
            dir
        else 
            dir + "/"
    let toRelativePath (relativeFrom:string) absolutePath = 
        let uri1 = new Uri(absolutePath)
        let uri2 = new Uri(relativeFrom |> ensureDirSeparator)        
        uri2.MakeRelativeUri(uri1).ToString()