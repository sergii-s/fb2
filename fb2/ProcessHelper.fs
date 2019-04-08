namespace IncrementalBuild

open System.Diagnostics

module ProcessHelper =
    let run name args dir =
        let proc = new Process()
        proc.StartInfo.FileName <- name
        proc.StartInfo.Arguments <- args
        proc.StartInfo.WorkingDirectory <- dir
        proc.StartInfo.UseShellExecute <- false
        proc.StartInfo.RedirectStandardOutput <- true
        proc.StartInfo.RedirectStandardError <- true
        proc.Start() |> ignore
        proc.WaitForExit()
        proc.StandardOutput.ReadToEnd()
        
