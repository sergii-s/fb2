namespace IncrementalBuild

module Git =

    let getCommits repo count = 
        ProcessHelper.run "git" (sprintf "log -%i --pretty=format:'%%h'" count) repo
            |> String.split "\n"
            |> Array.map (String.trim '\'')

    let getDiffFiles repo commit1 commit2 =
        ProcessHelper.run "git" (sprintf "diff --name-only %s %s" commit1 commit2) repo
            |> String.split "\n"
