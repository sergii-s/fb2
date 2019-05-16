namespace IncrementalBuild
open System

module String =
    let toLowerInvariant (s : string) =
        s.ToLowerInvariant()
    let split (splitWith : string) (s : string) =
        s.Split([|splitWith|], StringSplitOptions.RemoveEmptyEntries)
    let trim (trimChar : char) (s : string) =
        s.Trim(trimChar)
    let join (separator : string) (strings : string seq) =
        String.Join(separator, strings)
    let replace (replaceWhat:string) (replaceBy:string) (value:string) =
        value.Replace(replaceWhat, replaceBy)