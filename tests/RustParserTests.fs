module tests.RustParserTests

open System.IO
open IncrementalBuild
open IncrementalBuild.Model
open NFluent
open Xunit

[<Fact>]
let ``Check parsed TOML`` () =
    let project = RustProjectParser.parseProjectFile "." ("Samples/Cargo.toml" |> Path.GetFullPath)
    Check.That(project.IsSome).IsTrue |> ignore

    Check.That(project.Value.Name).Equals("my-project") |> ignore
    Check.That(project.Value.AssemblyName).Equals("my-project") |> ignore

    Check.That(project.Value.TargetFramework).Equals("2018") |> ignore
    Check.That(project.Value.TargetFramework).Equals("2018") |> ignore

    Check.That(project.Value.ProjectReferences.Length).Equals(3) |> ignore
    Check.That(project.Value.ProjectReferences.[0]).Equals("../bidder_contract") |> ignore
    Check.That(project.Value.ProjectReferences.[1]).Equals("../../Shared/rust-protobuf-schema") |> ignore
    Check.That(project.Value.ProjectReferences.[2]).Equals("../../library/pubsub") |> ignore
