module Test

open NUnit.Framework
open Volight.Ulid

[<SetUp>]
let Setup () =
    ()

[<Test>]
let TestUlid1 () =
    let id = Ulid.NewUlid()
    let str = id.ToString()
    let id2 = Ulid(str)
    Assert.AreEqual(id, id2)

[<Test>]
let TestUlid2 () =
    let id = Ulid.NewUlid()
    let guid = id.ToGuid()
    let id2 = Ulid.FromGuid(guid)
    Assert.AreEqual(id, id2)

[<Test>]
let TestSlid1 () =
    let id = Slid.NewSlid()
    let str = id.ToString()
    let id2 = Slid(str)
    Assert.AreEqual(id, id2)