namespace Volight.Ulid

open System.Runtime.CompilerServices
open Microsoft.FSharp.NativeInterop
open System.Security.Cryptography
open System.Diagnostics
open System.Threading
open System.Buffers
open System.Linq
open System

#nowarn "9"

module internal Utils =
    let lookup =
        let lookup = Array.create 128 -1
        for c, i in "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Select(fun a b -> struct (a, b)) do
            lookup.[int c] <- i
        lookup

exception UlidInvalidCharException of char

[<Struct; IsReadOnly; DebuggerDisplay(@"Ulid \{ {_DebugString(),nq} \}")>]
type Ulid private (lower: uint64, upper: uint64) =
    static let mutable lastTime = 0UL
    static let mutable lastRandom = struct (0us, 0UL)
    static let monotonicLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    static let rng = new ThreadLocal<_>(fun () -> new RNGCryptoServiceProvider())

    static member Empty = Ulid(0UL, 0UL)
    static member NewUlid() =
        let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        let timebits = uint64 (timestamp &&& 0x00_00_FF_FF_FF_FF_FF_FFL)
        let r1, r2 = 
            if timebits = Interlocked.Exchange(&lastTime, timebits) then 
                monotonicLock.EnterWriteLock()
                try
                    let struct (lo, up) = lastRandom
                    let x = lo + 1us
                    let up = if x < lo then up + 1UL else up
                    lastRandom <- struct (x, up)
                    monotonicLock.ExitWriteLock()
                    x, up
                with
                | _ ->
                    monotonicLock.ExitWriteLock()
                    reraise()
            else 
                monotonicLock.EnterReadLock()
                try
                    let rbptr = NativePtr.stackalloc<byte>(10) |> NativePtr.toVoidPtr
                    let rspan = Span<byte>(rbptr, 10)
                    rng.Value.GetBytes(rspan)
                    let rspan = ReadOnlySpan<byte>(rbptr, 10)
                    let r1 = BitConverter.ToUInt16(rspan.Slice(0, 2))
                    let r2 = BitConverter.ToUInt64(rspan.Slice(2))
                    lastRandom <- struct (r1, r2)
                    monotonicLock.ExitReadLock()
                    r1, r2
                with
                | _ ->
                    monotonicLock.ExitReadLock()
                    reraise()
        let t = (timebits <<< 16) ||| (uint64 r1)
        Ulid(t, r2)
    static member New() = Ulid.NewUlid()
    member _.ToBytes() =
        let bytes = Array.zeroCreate(16)
        let span = Span<byte>(bytes)
        BitConverter.TryWriteBytes(span.Slice(0, 8), lower) |> ignore
        BitConverter.TryWriteBytes(span.Slice(8, 8), upper) |> ignore
        bytes
    member _.WriteBytes(span: Span<byte>) =
        BitConverter.TryWriteBytes(span.Slice(0, 8), lower) |> ignore
        BitConverter.TryWriteBytes(span.Slice(8, 8), upper) |> ignore
    static member FromBytes(span: ReadOnlySpan<byte>) =
        let lower = BitConverter.ToUInt64(span.Slice(0, 8))
        let upper = BitConverter.ToUInt64(span.Slice(8, 8))
        Ulid(lower, upper)
    static member FromBytes(bytes: byte []) = Ulid.FromBytes(ReadOnlySpan<byte>(bytes))
    member _.TimeStamp = lower >>> 16
    member self.DateTimeOffset = 
        let stamp = self.TimeStamp
        DateTimeOffset.FromUnixTimeMilliseconds(int64 stamp)
    member self.DateTime = 
        self.DateTimeOffset.DateTime
    member _.ToGuid() =
        let bytes = ArrayPool.Shared.Rent(16)
        try
            let span = Span<byte>(bytes)
            BitConverter.TryWriteBytes(span.Slice(0, 8), lower) |> ignore
            BitConverter.TryWriteBytes(span.Slice(8, 8), upper) |> ignore
            Guid(ReadOnlySpan<byte>(bytes, 0, 16))
        finally
            ArrayPool.Shared.Return(bytes)
    static member FromGuid(guid: Guid) =
        let bytes = ArrayPool.Shared.Rent(16)
        try
            guid.TryWriteBytes(Span<byte>(bytes).Slice(0, 16)) |> ignore
            Ulid.FromBytes(bytes)
        finally
            ArrayPool.Shared.Return(bytes)
    override _.ToString() = 
        let mutable lo, up = lower, upper
        let buffer = ArrayPool.Shared.Rent(26)
        try
            for i = 0 to 25 do
                buffer.[25 - i] <- "0123456789ABCDEFGHJKMNPQRSTVWXYZ".[int ((uint lo) &&& 31u)]
                lo <- (lo >>> 5 ) ||| (up <<< 59)
                up <- up >>> 5
            String(ReadOnlySpan<char>(buffer, 0, 26))
        finally
            ArrayPool.Shared.Return(buffer)
    member private __._DebugString() = __.ToString()
    new(str: ReadOnlySpan<char>) =
        let span = str.Slice(0, 26)
        let mutable lo, up = 0UL, 0UL
        for i = 0 to 25 do
            let c = span.[i]
            if (c > 'z') then raise <| UlidInvalidCharException c
            let n = Utils.lookup.[int c]
            if (n < 0) then raise <| UlidInvalidCharException c
            up <- (up <<< 5) ||| (lo >>> 59)
            lo <- (lo <<< 5) ||| (uint64 n)
        Ulid(lo, up)
    new(str: Span<char>) = Ulid(Span<_>.op_Implicit(str))
    new(str: char []) = Ulid(ReadOnlySpan<_>(str))
    new(str: string) = Ulid(str.AsSpan())
    static member Parse(str: ReadOnlySpan<char>) = Ulid(str)
    static member Parse(str: Span<char>) = Ulid(Span<_>.op_Implicit(str))
    static member Parse(str: char []) = Ulid(ReadOnlySpan<_>(str))
    static member Parse(str: string) = Ulid(str.AsSpan())
    static member private TryParseLoop(span: ReadOnlySpan<char>, i: int, lo: uint64, up: uint64, ulid: Ulid outref) =
        if i > 25 then 
            ulid <- Ulid(lo, up)
            true
        else
            let c = span.[i]
            if (c > 'z') then false else
            let n = Utils.lookup.[int c]
            if (n < 0) then false else
            let up = (up <<< 5) ||| (lo >>> 59)
            let lo = (lo <<< 5) ||| (uint64 n)
            Ulid.TryParseLoop(span, i + 1, lo, up, &ulid)
    static member TryParse(str: ReadOnlySpan<char>, ulid: Ulid outref) = 
        if str.Length < 26 then false else Ulid.TryParseLoop(str, 0, 0UL, 0UL, &ulid)
    static member TryParse(str: Span<char>, ulid: Ulid outref) = Ulid.TryParse(Span<_>.op_Implicit(str), &ulid)
    static member TryParse(str: char [], ulid: Ulid outref) = Ulid.TryParse(ReadOnlySpan<_>(str), &ulid)
    static member TryParse(str: string, ulid: Ulid outref) = Ulid.TryParse(str.AsSpan(), &ulid)
    
[<Struct; IsReadOnly; DebuggerDisplay(@"Slid \{ {_DebugString(),nq} \}")>]
type Slid (value: uint64) =
    static let mutable lastTime = 0UL
    static let mutable lastRandom = 0u
    static let rng = new ThreadLocal<_>(fun () -> new RNGCryptoServiceProvider())

    static member Empty = Slid(0UL)
    static member NewSlid() =
        let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        let timebits = uint64 (timestamp &&& 0x00_00_FF_FF_FF_FF_FF_FFL)
        let r1 = 
            if timebits = Interlocked.Exchange(&lastTime, timebits) then 
                uint16 <| Interlocked.Increment(&lastRandom)
            else
                Interlocked.MemoryBarrier()
                let rbptr = NativePtr.stackalloc<byte>(2) |> NativePtr.toVoidPtr
                rng.Value.GetBytes(Span<byte>(rbptr, 2))
                let r1 = BitConverter.ToUInt16(ReadOnlySpan<byte>(rbptr, 2))
                Interlocked.Exchange(&lastRandom, uint32 r1) |> ignore
                r1
        let t = timebits ||| ((uint64 r1) <<< 48)
        Slid(t)
    static member New() = Slid.NewSlid()
    member _.ToBytes() =
        let bytes = Array.zeroCreate(8)
        let span = Span<byte>(bytes)
        BitConverter.TryWriteBytes(span, value) |> ignore
        bytes
    member _.WriteBytes(span: Span<byte>) =
        BitConverter.TryWriteBytes(span.Slice(0, 8), value) |> ignore
    member _.ToULong() = value
    static member FromULong(value: uint64) = Slid(value)
    static member FromBytes(span: ReadOnlySpan<byte>) =
        let value = BitConverter.ToUInt64(span.Slice(0, 8))
        Slid(value)
    static member FromBytes(bytes: byte []) = Ulid.FromBytes(ReadOnlySpan<byte>(bytes))
    member _.TimeStamp = value &&& 0x00_00_FF_FF_FF_FF_FF_FFUL
    member self.DateTimeOffset = 
        let stamp = self.TimeStamp
        DateTimeOffset.FromUnixTimeMilliseconds(int64 stamp)
    member self.DateTime = 
        self.DateTimeOffset.DateTime
    override _.ToString() = 
        let mutable va = value
        let buffer = ArrayPool.Shared.Rent(13)
        try
            for i = 0 to 12 do
                buffer.[12 - i] <- "0123456789ABCDEFGHJKMNPQRSTVWXYZ".[int (va &&& 31UL)]
                va <- va >>> 5
            String(ReadOnlySpan<char>(buffer, 0, 13))
        finally
            ArrayPool.Shared.Return(buffer)
    member private __._DebugString() = __.ToString()
    new (str: ReadOnlySpan<char>) = 
        let span = str.Slice(0, 13)
        let mutable va = 0UL
        for i = 0 to 12 do
            let c = span.[i]
            if (c > 'z') then raise <| UlidInvalidCharException c
            let n = Utils.lookup.[int c]
            if (n < 0) then raise <| UlidInvalidCharException c
            va <- (va <<< 5) ||| (uint64 n)
        Slid(va)
    new(str: Span<char>) = Slid(Span<_>.op_Implicit(str))
    new(str: char []) = Slid(ReadOnlySpan<_>(str))
    new(str: string) = Slid(str.AsSpan())
    static member Parse(str: ReadOnlySpan<char>) = Slid(str)
    static member Parse(str: Span<char>) = Slid(Span<_>.op_Implicit(str))
    static member Parse(str: char []) = Slid(ReadOnlySpan<_>(str))
    static member Parse(str: string) = Slid(str.AsSpan())
    static member private TryParseLoop(span: ReadOnlySpan<char>, i: int, va: uint64, slid: Slid outref) =
        if i > 12 then 
            slid <- Slid(va)
            true
        else
            let c = span.[i]
            if (c > 'z') then false else
            let n = Utils.lookup.[int c]
            if (n < 0) then false else
            Slid.TryParseLoop(span, i + 1,  (va <<< 5) ||| (uint64 n), &slid)
    static member TryParse(str: ReadOnlySpan<char>, slid: Slid outref) = 
        if str.Length < 13 then false else Slid.TryParseLoop(str, 0, 0UL, &slid)
    static member TryParse(str: Span<char>, slid: Slid outref) = Slid.TryParse(Span<_>.op_Implicit(str), &slid)
    static member TryParse(str: char [], slid: Slid outref) = Slid.TryParse(ReadOnlySpan<_>(str), &slid)
    static member TryParse(str: string, slid: Slid outref) = Slid.TryParse(str.AsSpan(), &slid)

[<AutoOpen>]
module Ulid =
    let ulid() = Ulid.NewUlid()
    
    type Guid with
        member self.ToUlid() = Ulid.FromGuid(self)

[<AutoOpen>]
module Slid =
    let slid() = Slid.NewSlid()

    type UInt64 with
        member self.ToSlid() = Slid(self)
