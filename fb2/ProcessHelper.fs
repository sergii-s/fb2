namespace IncrementalBuild

open System.Diagnostics

module ProcessHelper =
    let run dir args =
        let escapedArgs =
            args |> String.replace "\"" "\\\"" 
        let proc = new Process()
        proc.StartInfo.FileName <- "/bin/bash"
        proc.StartInfo.Arguments <- sprintf  "-c \"%s\"" escapedArgs
        proc.StartInfo.WorkingDirectory <- dir
        proc.StartInfo.UseShellExecute <- false
        proc.StartInfo.RedirectStandardOutput <- true
        proc.StartInfo.RedirectStandardError <- true
        proc.Start() |> ignore
        proc.WaitForExit()
        proc.StandardOutput.ReadToEnd()