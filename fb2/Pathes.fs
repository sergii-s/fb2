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