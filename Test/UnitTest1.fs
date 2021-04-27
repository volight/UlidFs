module Test

open NUnit.Framework
open Volight.Ulid
open System.Threading

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
let TestUlid3 () =
    let r = seq {
        for _ = 0 to 100 do 
        yield async {
            for _ = 0 to 100 do
                let id = Ulid.NewUlid()
                printfn "%s" (id.ToString())
            ()
        }
    }
    async {
        for i in r do
            let! () = i
            ()
    } |> Async.StartImmediateAsTask |> System.Threading.Tasks.Task.WaitAll

[<Test>]
let TestUlidP () =
    let r = seq {
        for _ = 0 to 100 do 
        yield async {
            for _ = 0 to 100 do
                let id = Ulid.NewUlid()
                printfn "%s" (id.Prettify())
            ()
        }
    }
    async {
        for i in r do
            let! () = i
            ()
    } |> Async.StartImmediateAsTask |> System.Threading.Tasks.Task.WaitAll

[<Test>]
let TestSlid1 () =
    let id = Slid.NewSlid()
    let str = id.ToString()
    let id2 = Slid(str)
    Assert.AreEqual(id, id2)

[<Test>]
let TestSlid2 () =
    let r = seq {
        for _ = 0 to 100 do 
        yield async {
            for _ = 0 to 100 do
                let id = Slid.NewSlid()
                printfn "%s" (id.ToString())
            ()
        }
    }
    async {
        for i in r do
            let! () = i
            ()
    } |> Async.StartImmediateAsTask |> System.Threading.Tasks.Task.WaitAll

[<Test>]
let TestSlid3 () =
    let r = seq {
        for _ = 0 to 100 do 
        yield async {
            for _ = 0 to 100 do
                let id = Slid.NewSlid()
                printfn "%s" (id.Lexic())
            ()
        }
    }
    async {
        for i in r do
            let! () = i
            ()
    } |> Async.StartImmediateAsTask |> System.Threading.Tasks.Task.WaitAll