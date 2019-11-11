namespace fb2.UnitTests
open IncrementalBuild
open Xunit
open Model

module SnapshotTests =
    open System
    open System.IO
    open NFluent

    [<Fact>]
    let ``Should create new snapshot file if old one not exists`` () =
        ()
        
    [<Fact>]
    let ``Should fail to create new snapshot file if build is partial`` () =
        ()