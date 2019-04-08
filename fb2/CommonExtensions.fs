namespace IncrementalBuild
open System

module String =
    let toLowerInvariant (s : string) =
        s.ToLowerInvariant()
    let split (splitWith : string) (s : string) =
        s.Split([|splitWith|], StringSplitOptions.RemoveEmptyEntries)
    let trim (trimChar : char) (s : string) =
        s.Trim(trimChar)
