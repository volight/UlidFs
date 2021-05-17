using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using static Volight.Ulid.Utils;

namespace Volight.Ulid
{
    static class Utils
    {
        public static int[] lookup;

        static Utils()
        {
            lookup = Enumerable.Repeat(-1, 128).ToArray();
            foreach (var (c, i) in "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Select((a, b) => (a, b)))
                lookup[c] = i;
        }

        public static IEnumerable<int> Range(int from, int to) => Enumerable.Range(from, to + 1);
    }

    [Serializable]
    public class UlidInvalidCharException : Exception
    {
        public char Char { get; set; }
        public UlidInvalidCharException(char c) { Char = c; }
        public UlidInvalidCharException(char c, string message) : base(message) { Char = c; }
        public UlidInvalidCharException(char c, string message, Exception inner) : base(message, inner) { Char = c; }
        protected UlidInvalidCharException(char c, SerializationInfo info, StreamingContext context) : base(info, context) { Char = c; }
    }

    [Serializable]
    [DebuggerDisplay(nameof(Ulid) + " { " + nameof(GetDebuggerDisplay) + "(),nq }")]
    public readonly struct Ulid : IEquatable<Ulid>
    {
        public readonly ulong lower;
        public readonly ulong upper;

        Ulid(ulong lower, ulong upper)
        {
            this.lower = lower;
            this.upper = upper;
        }

        static long lastTime = 0;
        static (ushort, ulong) lastRandom = (0, 0);
        static readonly ReaderWriterLockSlim monotonicLock = new(LockRecursionPolicy.NoRecursion);
        static readonly ThreadLocal<RNGCryptoServiceProvider> rng = new(() => new());

        public static readonly Ulid Empty = new(0, 0);
        public static Ulid NewUlid()
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timebits = (timestamp & 0x00_00_FF_FF_FF_FF_FF_FFL) << 16;
            ushort r1;
            ulong r2;
            if (timebits == Interlocked.Exchange(ref lastTime, timebits))
            {
                var locked = monotonicLock.TryEnterWriteLock(1);
                try
                {
                    var (lo, up) = lastRandom;
                    r2 = up + 1UL;
                    r1 = (ushort)(r2 < up ? lo + 1 : lo);
                    lastRandom = (r1, r2);
                }
                finally
                {
                    if (locked) monotonicLock.ExitWriteLock();
                }
            }
            else
            {
                var locked = monotonicLock.TryEnterWriteLock(1);
                try
                {
                    Span<byte> rspan = stackalloc byte[10];
                    rng.Value.GetBytes(rspan);
                    r1 = BitConverter.ToUInt16(rspan.Slice(0, 2));
                    r2 = BitConverter.ToUInt64(rspan[2..]);
                    lastRandom = (r1, r2);
                }
                finally
                {
                    if (locked) monotonicLock.ExitWriteLock();
                }
            }
            var t = (ulong)timebits | r1;
            return new Ulid(r2, t);
        }
        public static Ulid New() => NewUlid();

        public byte[] ToBytes()
        {
            var bytes = new byte[16];
            WriteBytes(bytes);
            return bytes;
        }
        public void WriteBytes(Span<byte> span)
        {
            var self = this;
            MemoryMarshal.Write(span, ref self);
        }

        public static Ulid FromBytes(ReadOnlySpan<byte> span) => MemoryMarshal.Read<Ulid>(span);
        public static Ulid FromBytes(byte[] bytes) => FromBytes(new ReadOnlySpan<byte>(bytes));

        public ulong TimeStamp => upper >> 16;
        public long STimeStamp => (long)TimeStamp;

        public DateTimeOffset DateTimeOffset => DateTimeOffset.FromUnixTimeMilliseconds(STimeStamp);
        public DateTime DateTime => DateTimeOffset.DateTime;

        public Guid ToGuid()
        {
            var bytes = ArrayPool<byte>.Shared.Rent(16);
            try
            {
                WriteBytes(bytes);
                return new Guid(new ReadOnlySpan<byte>(bytes, 0, 16));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }
        public static Ulid FromGuid(Guid guid)
        {
            var bytes = ArrayPool<byte>.Shared.Rent(16);
            try
            {
                guid.TryWriteBytes(new Span<byte>(bytes).Slice(0, 16));
                return FromBytes(bytes);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        public override string ToString()
        {
            var (lo, up) = (lower, upper);
            var buffer = ArrayPool<char>.Shared.Rent(26);
            try
            {
                foreach (var i in Range(0, 25))
                {
                    buffer[25 - i] = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"[(int)((uint)lo & 31u)];
                    lo = (lo >> 5) | (up << 59);
                    up >>= 5;
                }
                return new string(new ReadOnlySpan<char>(buffer, 0, 26));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        private string GetDebuggerDisplay() => ToString();

        public string Prettify()
        {
            var (lo, up) = (lower, upper);
            var buffer = ArrayPool<char>.Shared.Rent(26);
            try
            {
                foreach (var i in Range(0, 15))
                {
                    buffer[15 - i] = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"[(int)((uint)lo & 31u)];
                    lo = (lo >> 5) | (up << 59);
                    up >>= 5;
                }
                foreach (var i in Range(0, 9))
                {
                    buffer[25 - i] = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"[(int)((uint)lo & 31u)];
                    lo = (lo >> 5) | (up << 59);
                    up >>= 5;
                }
                return new string(new ReadOnlySpan<char>(buffer, 0, 26));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        public Ulid(ReadOnlySpan<char> str)
        {
            var span = str.Slice(0, 26);
            ulong lo = 0UL, up = 0UL;
            foreach (var i in Range(0, 25))
            {
                var c = span[i];
                if (c > 'z') throw new UlidInvalidCharException(c);
                var n = lookup[c];
                if (n < 0) throw new UlidInvalidCharException(c);
                var n2 = (ulong)n;
                up = (up << 5) | (lo >> 59);
                lo = (lo << 5) | (n2);
            }
            this = new Ulid(lo, up);
        }
        public Ulid(Span<char> str) : this((ReadOnlySpan<char>)str) { }
        public Ulid(char[] str) : this(new ReadOnlySpan<char>(str)) { }
        public Ulid(string str) : this(str.AsSpan()) { }

        public static Ulid Parse(ReadOnlySpan<char> str) => new(str);
        public static Ulid Parse(Span<char> str) => new(str);
        public static Ulid Parse(char[] str) => new(str);
        public static Ulid Parse(string str) => new(str);

        public static bool TryParse(ReadOnlySpan<char> str, out Ulid ulid)
        {
            if (str.Length < 26) { ulid = default; return false; }
            ulong lo = 0UL, up = 0UL;
            foreach (var i in Range(0, 25))
            {
                var c = str[i];
                if (c > 'z') { ulid = default; return false; }
                var n = lookup[c];
                if (n < 0) { ulid = default; return false; }
                var n2 = (ulong)n;
                up = (up << 5) | (lo >> 59);
                lo = (lo << 5) | (n2);
            }
            ulid = new Ulid(lo, up);
            return true;
        }
        public static bool TryParse(Span<char> str, out Ulid ulid) => TryParse((ReadOnlySpan<char>)str, out ulid);
        public static bool TryParse(char[] str, out Ulid ulid) => TryParse(new ReadOnlySpan<char>(str), out ulid);
        public static bool TryParse(string str, out Ulid ulid) => TryParse(str.AsSpan(), out ulid);

        public override bool Equals(object obj) => obj is Ulid ulid && Equals(ulid);

        public bool Equals(Ulid other) => lower == other.lower && upper == other.upper;
        public override int GetHashCode() => HashCode.Combine(lower, upper);

        public static bool operator ==(Ulid left, Ulid right) => left.Equals(right);

        public static bool operator !=(Ulid left, Ulid right) => !(left == right);
    }

    [Serializable]
    [DebuggerDisplay(nameof(Slid) + " { " + nameof(GetDebuggerDisplay) + "(),nq }")]
    public readonly struct Slid
    {
        private readonly ulong value;

        public Slid(ulong value)
        {
            this.value = value;
        }

        static long lastTime = 0;
        static int lastRandom = 0;
        static readonly ReaderWriterLockSlim monotonicLock = new(LockRecursionPolicy.NoRecursion);
        static readonly ThreadLocal<RNGCryptoServiceProvider> rng = new(() => new());

        public static Slid Empty = new(0);

        public static Slid NewSlid()
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timebits = timestamp & 0x00_00_FF_FF_FF_FF_FF_FFL;
            ushort r1;
            if (timebits == Interlocked.Exchange(ref lastTime, timebits))
            {
                r1 = (ushort)Interlocked.Increment(ref lastRandom);
            }
            else
            {
                Interlocked.MemoryBarrier();
                Span<byte> rbspan = stackalloc byte[2];
                rng.Value.GetBytes(rbspan);
                r1 = BitConverter.ToUInt16(rbspan);
                Interlocked.Exchange(ref lastRandom, r1);
            }
            var t = (ulong)timebits | ((ulong)r1 << 48);
            return new Slid(t);
        }
        public static Slid New() => NewSlid();

        public byte[] ToBytes()
        {
            var bytes = new byte[8];
            WriteBytes(bytes);
            return bytes;
        }
        public void WriteBytes(Span<byte> span) => BitConverter.TryWriteBytes(span.Slice(0, 8), value);
        public ulong ToULong() => value;
        public static Slid FromULong(ulong value) => new(value);
        public static Slid FromBytes(ReadOnlySpan<byte> span) => new(BitConverter.ToUInt64(span.Slice(0, 8)));
        public static Slid FromBytes(byte[] bytes) => FromBytes(new ReadOnlySpan<byte>(bytes));

        public ulong TimeStamp => value & 0x00_00_FF_FF_FF_FF_FF_FFUL;
        public long STimeStamp => (long)TimeStamp;

        public DateTimeOffset DateTimeOffset => DateTimeOffset.FromUnixTimeMilliseconds(STimeStamp);
        public DateTime DateTime => DateTimeOffset.DateTime;

        public override string ToString()
        {
            var va = value;
            var buffer = ArrayPool<char>.Shared.Rent(13);
            try
            {
                foreach (var i in Range(0, 12))
                {
                    buffer[12 - i] = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"[(int)(va & 31UL)];
                    va >>= 5;
                }
                return new string(new ReadOnlySpan<char>(buffer, 0, 13));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }
        private string GetDebuggerDisplay() => ToString();
        public string Lexic()
        {
            var va = value;
            var buffer = ArrayPool<char>.Shared.Rent(13);
            try
            {
                foreach (var i in Range(0, 8))
                {
                    buffer[8 - i] = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"[(int)(va & 31UL)];
                    va >>= 5;
                }
                foreach (var i in Range(0, 3))
                {
                    buffer[12 - i] = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"[(int)(va & 31UL)];
                    va >>= 5;
                }
                return new string(new ReadOnlySpan<char>(buffer, 0, 13));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        public Slid(ReadOnlySpan<char> str)
        {
            var span = str.Slice(0, 13);
            var va = 0UL;
            foreach (var i in Range(0, 12))
            {
                var c = span[i];
                if (c > 'z') throw new UlidInvalidCharException(c);
                var n = lookup[c];
                if (c < 0) throw new UlidInvalidCharException(c);
                var n2 = (ulong)n;
                va = (va << 5) | n2;
            }
            this = new Slid(va);
        }
        public Slid(Span<char> str) : this((ReadOnlySpan<char>)str) { }
        public Slid(char[] str) : this(new ReadOnlySpan<char>(str)) { }
        public Slid(string str) : this(str.AsSpan()) { }

        public static Slid Parse(ReadOnlySpan<char> str) => new(str);
        public static Slid Parse(Span<char> str) => new(str);
        public static Slid Parse(char[] str) => new(str);
        public static Slid Parse(string str) => new(str);

        public static bool TryParse(ReadOnlySpan<char> str, out Slid slid)
        {
            if (str.Length < 13) { slid = default; return false; }
            var va = 0UL;
            foreach (var i in Range(0, 12))
            {
                var c = str[i];
                if (c > 'z') { slid = default; return false; }
                var n = lookup[c];
                if (c < 0) { slid = default; return false; }
                var n2 = (ulong)n;
                va = (va << 5) | n2;
            }
            slid = new Slid(va);
            return true;
        }
        public static bool TryParse(Span<char> str, out Slid slid) => TryParse((ReadOnlySpan<char>)str, out slid);
        public static bool TryParse(char[] str, out Slid slid) => TryParse(new ReadOnlySpan<char>(str), out slid);
        public static bool TryParse(string str, out Slid slid) => TryParse(str.AsSpan(), out slid);
    }

    public static class UlidEx
    {
        public static Ulid ToUlid(this Guid guid) => Ulid.FromGuid(guid);
    }
}
