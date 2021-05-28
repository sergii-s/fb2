module tests.DotNetProjectParserTests

open IncrementalBuild
open NFluent
open Xunit

[<Fact>]
let ``Check references are using the right path`` () =
    let project = DotnetProjectParser.parseProjectFile "/work/projects" "Samples/Test.csproj"

    Check.That(project.IsSome).IsTrue() |> ignore
    Check.That(project.Value.ProjectReferences.Length).Equals(2) |> ignore

    ()
