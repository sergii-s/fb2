namespace fb2.UnitTests
open IncrementalBuild
open NFluent
open Xunit

module CommonToolsTests =
    [<Fact>]
    let ``Path - To relative - from absolute`` () =
        let p = "./folder/file1.txt" |> Pathes.toRelativePath "./"
        Check.That(p).IsEqualTo("folder/file1.txt")
        
    [<Fact>]
    let ``Path - To relative - from relative`` () =
        let p = "/home/user/folder/file1.txt" |> Pathes.toRelativePath "/home/user"
        Check.That(p).IsEqualTo("folder/file1.txt")