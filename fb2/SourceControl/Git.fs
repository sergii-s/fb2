namespace IncrementalBuild

module Git =

    let getCommits repo count =
        sprintf "git log -%i --pretty=format:'%%H'" count
            |> ProcessHelper.run repo
            |> String.split "\n"
            |> Array.map (String.trim '\'')
            |> List.ofSeq

    let getDiffFiles repo commit1 commit2 =
        sprintf "git diff --name-only %s %s" commit1 commit2
            |> ProcessHelper.run repo
            |> String.split "\n"
            
    let getBranch repo =
        "git branch | grep \\* | cut -d ' ' -f2" 
            |> ProcessHelper.run repo
            |> String.trim '\n'
