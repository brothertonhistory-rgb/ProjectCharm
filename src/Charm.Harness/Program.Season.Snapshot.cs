using System.Collections;
using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S127 — A SEASON SAVED MID-WAY: THE SNAPSHOT (save design B2a, K1 and K8).
//
//  The season's whole state is one object graph rooted at its SeasonRun. This file
//  stores that graph and builds it back, by reflection over every instance field of
//  everything reachable — private fields and those of base types, each level's
//  fields in metadata order. The format is TIED TO THE BUILD: a save is only ever
//  loaded by the build that wrote it (the career's `progress` records the build
//  stamp and a resume under another is refused), so a field added to any season
//  type is stored by the next build with no format work. Whether anything was
//  missed is not this file's claim; it is Phase 118's proof (a season saved on any
//  day and resumed in a fresh program finishes byte-identical).
//
//  ★ WRITING. Every REFERENCE OBJECT (a class or record instance, an array, a list,
//  a table) gets an id the first time it is met and is written once; every later
//  meeting writes the id. Identity is by reference (ReferenceEqualityComparer), never
//  by Equals, so two equal-looking objects stay two and one shared object stays one
//  (the results list every conference tournament reads; a man on a roster and in the
//  pool). Strings are values. A VALUE TYPE (a struct, a value tuple, a nullable) has
//  no id: it is written inline, field by field, and a reference inside it is an id
//  like any other. Doubles and floats are written as their raw bits.
//
//  ★ RESTORING, IN THREE STAGES, BEFORE ANYTHING RUNS.
//    1. ALLOCATE every reference object empty — classes and records with no
//       constructor run, arrays at their rank and lengths, lists and tables by their
//       public constructor with the recorded comparer — and register it by id.
//    2. FILL every field, resolving ids through the table (a cycle or a shared list
//       resolves to the one object); arrays and lists in saved order.
//    3. REHASH: only now, every field in place, each Dictionary and HashSet is filled
//       in saved order — last, because a key's hash comes from its own fields.
//    The restore uses only public collection behaviour (constructor, comparer, Add in
//    saved order); it never copies .NET's internals.
//
//  ★ WHAT IT STORES, AND WHAT IT REFUSES — by name, with the field path:
//    values      bool, the integer types, char, enums, string, DateOnly, decimal,
//                double/float (raw bits), nullables, value tuples, structs of this
//                program's three assemblies;
//    references  classes and records of the three assemblies; arrays (any rank,
//                jagged); List<T>, Dictionary<K,V> and HashSet<T>, with the default
//                comparer, StringComparer.Ordinal or OrdinalIgnoreCase;
//                ★ FrozenDictionary<K,V> of pure values with the default comparer (the
//                person-number map — gate finding 4), stored as its entries in its own
//                order and rebuilt by its public builder;
//    rebound     the fields the caller supplies again on a resume (never stored);
//    refused     any other delegate; any object reachable from the world; a boxed
//                value; a stream, a lock, a handle — any other System type; any other
//                collection type until it is added here deliberately.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ S127 — what a snapshot's header carries, checked against the career's `progress`
    /// before the body is decoded (K8 step 1).</summary>
    private sealed record SeasonSnapshotHeader(
        string RulesStamp, string ConfigSha256, string HistoryId, long Season, string NextDay, long SeasonSeed,
        ProgressHeld Held);

    private static class SeasonSnapshot
    {
        //  The magic line: a snapshot is recognisable by its first bytes, whatever else is wrong with it.
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CHARM-SEASON-SNAPSHOT 1\n");

        private static readonly Assembly[] OwnAssemblies =
        {
            typeof(Charm.Engine.GovernorConfig).Assembly, typeof(HistoryStore).Assembly, typeof(Program).Assembly,
        };

        private enum Kind : byte { Class, Array, List, Dictionary, HashSet, Frozen }

        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // ── Shared rules ────────────────────────────────────────────────────────────

        /// <summary>Every instance field, base type first, each level in metadata order.</summary>
        private static FieldInfo[] FieldsOf(Type t, Dictionary<Type, FieldInfo[]> cache)
        {
            if (cache.TryGetValue(t, out var got)) return got;
            var levels = new List<Type>();
            for (var b = t; b is not null && b != typeof(object) && b != typeof(ValueType); b = b.BaseType) levels.Add(b);
            levels.Reverse();
            var fields = levels.SelectMany(l => l.GetFields(Instance).OrderBy(f => f.MetadataToken)).ToArray();
            cache[t] = fields;
            return fields;
        }

        private static bool IsOwn(Type t) => Array.IndexOf(OwnAssemblies, t.Assembly) >= 0;

        private static bool IsLeaf(Type t)
            => t == typeof(string) || t == typeof(decimal) || t == typeof(DateOnly) || t.IsEnum
               || (t.IsPrimitive && t != typeof(IntPtr) && t != typeof(UIntPtr));

        private static bool IsValueTuple(Type t)
            => t.IsValueType && t.IsGenericType && t.Namespace == "System"
               && t.GetGenericTypeDefinition().Name.StartsWith("ValueTuple`", StringComparison.Ordinal);

        /// <summary>A value type this file stores inline: a leaf, a nullable of one, a value tuple, or a struct of
        /// the three assemblies. Its fields are checked as they are written.</summary>
        private static bool IsStorableValueType(Type t)
            => IsLeaf(t)
               || (Nullable.GetUnderlyingType(t) is { } u && IsStorableValueType(u))
               || IsValueTuple(t)
               || (t.IsValueType && IsOwn(t));

        /// <summary>A value type with no reference anywhere inside (strings count as values).</summary>
        private static bool IsPureValue(Type t, HashSet<Type>? seen = null)
        {
            if (IsLeaf(t)) return true;
            if (Nullable.GetUnderlyingType(t) is { } u) return IsPureValue(u, seen);
            if (!t.IsValueType || !(IsValueTuple(t) || IsOwn(t))) return false;
            seen ??= new HashSet<Type>();
            if (!seen.Add(t)) return true;
            for (var b = t; b is not null && b != typeof(ValueType); b = b.BaseType)
                foreach (var f in b.GetFields(Instance))
                    if (!IsPureValue(f.FieldType, seen)) return false;
            return true;
        }

        private static Type? FrozenBase(Type t)
        {
            for (var b = t; b is not null; b = b.BaseType)
                if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(FrozenDictionary<,>)) return b;
            return null;
        }

        /// <summary>The kind a reference object is stored as, or the reason it is refused.</summary>
        private static (Kind Kind, Type Stored)? Classify(Type t, out string? refusal)
        {
            refusal = null;
            if (typeof(Delegate).IsAssignableFrom(t)) { refusal = "a delegate"; return null; }
            if (t.IsValueType) { refusal = $"a boxed value ({t.Name})"; return null; }
            if (t.IsArray)
            {
                var e = t.GetElementType()!;
                if (e.IsValueType && !IsStorableValueType(e)) { refusal = $"an array of {e.FullName}"; return null; }
                return (Kind.Array, t);
            }
            if (t.IsGenericType)
            {
                var g = t.GetGenericTypeDefinition();
                var args = t.GetGenericArguments();
                if (args.Any(a => a.IsValueType && !IsStorableValueType(a)))
                { refusal = $"a {g.Name} of a type this file does not store"; return null; }
                if (g == typeof(List<>)) return (Kind.List, t);
                if (g == typeof(Dictionary<,>)) return (Kind.Dictionary, t);
                if (g == typeof(HashSet<>)) return (Kind.HashSet, t);
            }
            if (FrozenBase(t) is { } fb)
            {
                var a = fb.GetGenericArguments();
                if (!IsPureValue(a[0]) || !IsPureValue(a[1]))
                { refusal = "a frozen dictionary holding references"; return null; }
                return (Kind.Frozen, fb);
            }
            if (IsOwn(t) && !t.IsAbstract) return (Kind.Class, t);
            refusal = $"a {t.FullName}, a type this file does not store";
            return null;
        }

        /// <summary>0 default, 1 Ordinal, 2 OrdinalIgnoreCase; null for any other comparer.</summary>
        private static byte? ComparerCode(object? comparer, Type keyType)
        {
            if (comparer is null) return 0;
            var def = typeof(EqualityComparer<>).MakeGenericType(keyType).GetProperty("Default")!.GetValue(null);
            if (ReferenceEquals(comparer, def)) return 0;
            if (keyType == typeof(string) && ReferenceEquals(comparer, StringComparer.Ordinal)) return 1;
            if (keyType == typeof(string) && ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase)) return 2;
            return null;
        }

        private static object? ComparerFor(byte code, Type keyType) => code switch
        {
            0 => null,
            1 when keyType == typeof(string) => StringComparer.Ordinal,
            2 when keyType == typeof(string) => StringComparer.OrdinalIgnoreCase,
            _ => throw new InvalidDataException($"comparer code {code} is not one this file writes for {keyType.Name}"),
        };

        // ════════════════════════════════════════════════════════════════════════════
        //  WRITING
        // ════════════════════════════════════════════════════════════════════════════

        /// <summary>The bytes of <paramref name="root"/>'s whole graph under <paramref name="header"/>. Fields
        /// for which <paramref name="rebound"/> is true are not stored; an object in <paramref name="forbidden"/>
        /// (the world's) is refused by path.</summary>
        public static byte[] Write(SeasonSnapshotHeader header, object root, Func<FieldInfo, bool> rebound,
                                   IReadOnlySet<object> forbidden)
        {
            var writer = new GraphWriter(rebound, forbidden);
            writer.Run(root);
            using var ms = new MemoryStream();
            ms.Write(Magic);
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(header.RulesStamp);
                w.Write(header.ConfigSha256);
                w.Write(header.HistoryId);
                w.Write(header.Season);
                w.Write(header.NextDay);
                w.Write(header.SeasonSeed);
                w.Write(header.Held.NextPersonId);
                w.Write(header.Held.NextSeasonId);
                w.Write(header.Held.NextGameId);
                w.Write7BitEncodedInt(writer.Types.Count);
                foreach (var t in writer.Types) w.Write(t.AssemblyQualifiedName!);
                w.Write7BitEncodedInt(writer.ObjectCount);
            }
            writer.Table.WriteTo(ms);
            writer.Contents.WriteTo(ms);
            return ms.ToArray();
        }

        private sealed class GraphWriter
        {
            private readonly Func<FieldInfo, bool> _rebound;
            private readonly IReadOnlySet<object> _forbidden;
            private readonly Dictionary<Type, FieldInfo[]> _fields = new();
            private readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);
            private readonly List<object> _objects = new();
            private readonly List<(Kind Kind, Type Stored)> _kinds = new();
            private readonly List<(int Parent, string Label)> _from = new();
            private readonly Dictionary<Type, int> _typeIndex = new();
            public readonly List<Type> Types = new();
            public readonly MemoryStream Table = new(), Contents = new();
            private readonly BinaryWriter _table, _body;
            //  The path inside the object being written: field names and element indices, joined only when a
            //  new object is met (to record where it was found) or a refusal names it.
            private readonly List<string> _segments = new();
            private int _current = -1;

            public GraphWriter(Func<FieldInfo, bool> rebound, IReadOnlySet<object> forbidden)
            {
                _rebound = rebound;
                _forbidden = forbidden;
                _table = new BinaryWriter(Table, Encoding.UTF8, leaveOpen: true);
                _body = new BinaryWriter(Contents, Encoding.UTF8, leaveOpen: true);
            }

            public int ObjectCount => _objects.Count;

            public void Run(object root)
            {
                Id(root);
                for (var i = 0; i < _objects.Count; i++)
                {
                    _current = i;
                    _segments.Clear();
                    WriteContents(_objects[i], _kinds[i].Kind);
                }
                _table.Flush();
                _body.Flush();
            }

            private string Here()
            {
                var sb = new StringBuilder();
                foreach (var s in _segments) { if (sb.Length > 0 && s[0] != '[') sb.Append('.'); sb.Append(s); }
                return sb.ToString();
            }

            private string PathOf(int id)
            {
                var parts = new List<string>();
                for (var i = id; i >= 0; i = _from[i].Parent) parts.Add(_from[i].Label);
                parts.Reverse();
                var sb = new StringBuilder();
                foreach (var p in parts.Where(p => p.Length > 0))
                { if (sb.Length > 0 && p[0] != '[') sb.Append('.'); sb.Append(p); }
                return sb.ToString();
            }

            private InvalidOperationException Refuse(string what)
            {
                var path = _current < 0 ? "" : PathOf(_current);
                var here = Here();
                var full = here.Length == 0 ? path : path.Length == 0 ? here : path + (here[0] == '[' ? "" : ".") + here;
                return new InvalidOperationException($"SEASON SNAPSHOT: refused {what} at {full}.");
            }

            private int Id(object o)
            {
                if (_ids.TryGetValue(o, out var id)) return id;
                var t = o.GetType();
                if (Classify(t, out var why) is not { } k) throw Refuse(why!);
                if (_forbidden.Contains(o)) throw Refuse($"a {t.Name} that belongs to the world");
                id = _objects.Count;
                _ids[o] = id;
                _objects.Add(o);
                _kinds.Add(k);
                _from.Add((_current, _current < 0 ? t.Name : Here()));
                if (!_typeIndex.TryGetValue(k.Stored, out var ti)) { ti = Types.Count; Types.Add(k.Stored); _typeIndex[k.Stored] = ti; }
                _table.Write7BitEncodedInt(ti);
                switch (k.Kind)
                {
                    case Kind.Array:
                        var a = (Array)o;
                        _table.Write7BitEncodedInt(a.Rank);
                        for (var r = 0; r < a.Rank; r++) _table.Write7BitEncodedInt(a.GetLength(r));
                        break;
                    case Kind.List:
                        _table.Write7BitEncodedInt(((IList)o).Count);
                        break;
                    case Kind.Dictionary:
                    case Kind.HashSet:
                    {
                        var key = k.Kind == Kind.Dictionary ? t.GetGenericArguments()[0] : t.GetGenericArguments()[0];
                        var code = ComparerCode(t.GetProperty("Comparer")!.GetValue(o), key)
                                   ?? throw Refuse($"a {t.Name} with a comparer this file does not store");
                        _table.Write7BitEncodedInt((int)t.GetProperty("Count")!.GetValue(o)!);
                        _table.Write(code);
                        break;
                    }
                    case Kind.Frozen:
                    {
                        var args = k.Stored.GetGenericArguments();
                        var code = ComparerCode(k.Stored.GetProperty("Comparer")!.GetValue(o), args[0]);
                        if (code != 0) throw Refuse("a frozen dictionary with a comparer other than the default");
                        var entries = ((IEnumerable)o).Cast<object>().ToList();
                        _table.Write7BitEncodedInt(entries.Count);
                        var kvp = typeof(KeyValuePair<,>).MakeGenericType(args);
                        var kp = kvp.GetProperty("Key")!; var vp = kvp.GetProperty("Value")!;
                        foreach (var e in entries)
                        {
                            WriteValue(_table, args[0], kp.GetValue(e));
                            WriteValue(_table, args[1], vp.GetValue(e));
                        }
                        break;
                    }
                }
                return id;
            }

            private void WriteContents(object o, Kind kind)
            {
                var t = o.GetType();
                switch (kind)
                {
                    case Kind.Class:
                        foreach (var f in FieldsOf(t, _fields))
                        {
                            if (_rebound(f)) continue;
                            _segments.Add(Label(f));
                            WriteValue(_body, f.FieldType, f.GetValue(o));
                            _segments.RemoveAt(_segments.Count - 1);
                        }
                        break;
                    case Kind.Array:
                    {
                        var e = t.GetElementType()!;
                        var i = 0;
                        foreach (var x in (Array)o)   // row-major for every rank
                        {
                            _segments.Add("[" + i.ToString(CultureInfo.InvariantCulture) + "]");
                            WriteValue(_body, e, x);
                            _segments.RemoveAt(_segments.Count - 1);
                            i++;
                        }
                        break;
                    }
                    case Kind.List:
                    case Kind.HashSet:
                    {
                        var e = t.GetGenericArguments()[0];
                        var i = 0;
                        foreach (var x in (IEnumerable)o)
                        {
                            _segments.Add("[" + i.ToString(CultureInfo.InvariantCulture) + "]");
                            WriteValue(_body, e, x);
                            _segments.RemoveAt(_segments.Count - 1);
                            i++;
                        }
                        break;
                    }
                    case Kind.Dictionary:
                    {
                        var args = t.GetGenericArguments();
                        var i = 0;
                        foreach (DictionaryEntry de in (IDictionary)o)
                        {
                            _segments.Add("[" + i.ToString(CultureInfo.InvariantCulture) + "]");
                            WriteValue(_body, args[0], de.Key);
                            WriteValue(_body, args[1], de.Value);
                            _segments.RemoveAt(_segments.Count - 1);
                            i++;
                        }
                        break;
                    }
                    case Kind.Frozen:
                        break;   // written whole in the table
                }
            }

            private static string Label(FieldInfo f)
                => f.Name.StartsWith('<') && f.Name.IndexOf('>') is var gt and > 1 ? f.Name[1..gt] : f.Name;

            private void WriteValue(BinaryWriter w, Type declared, object? v)
            {
                if (declared == typeof(string))
                {
                    w.Write(v is not null);
                    if (v is not null) w.Write((string)v);
                    return;
                }
                if (declared.IsEnum) { WriteValue(w, Enum.GetUnderlyingType(declared), Convert.ChangeType(v!, Enum.GetUnderlyingType(declared), CultureInfo.InvariantCulture)); return; }
                if (declared.IsPrimitive)
                {
                    switch (v)
                    {
                        case bool b: w.Write(b); return;
                        case byte b: w.Write(b); return;
                        case sbyte b: w.Write(b); return;
                        case short b: w.Write(b); return;
                        case ushort b: w.Write(b); return;
                        case int b: w.Write(b); return;
                        case uint b: w.Write(b); return;
                        case long b: w.Write(b); return;
                        case ulong b: w.Write(b); return;
                        case char b: w.Write((ushort)b); return;
                        case float b: w.Write(BitConverter.SingleToInt32Bits(b)); return;
                        case double b: w.Write(BitConverter.DoubleToInt64Bits(b)); return;
                        default: throw Refuse($"a {declared.Name}");
                    }
                }
                if (declared == typeof(decimal)) { foreach (var part in decimal.GetBits((decimal)v!)) w.Write(part); return; }
                if (declared == typeof(DateOnly)) { w.Write(((DateOnly)v!).DayNumber); return; }
                if (Nullable.GetUnderlyingType(declared) is { } under)
                {
                    w.Write(v is not null);
                    if (v is not null) WriteValue(w, under, v);
                    return;
                }
                if (declared.IsValueType)
                {
                    if (!IsStorableValueType(declared)) throw Refuse($"a {declared.FullName}, a value type this file does not store");
                    foreach (var f in FieldsOf(declared, _fields))
                    {
                        _segments.Add(Label(f));
                        WriteValue(w, f.FieldType, f.GetValue(v));
                        _segments.RemoveAt(_segments.Count - 1);
                    }
                    return;
                }
                //  A reference slot: 0 null, 1 a string held where a reference is declared, else 2 + id.
                if (v is null) { w.Write7BitEncodedInt(0); return; }
                if (v is string s) { w.Write7BitEncodedInt(1); w.Write(s); return; }
                w.Write7BitEncodedInt(2 + Id(v));
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        //  READING (K8 steps 1 and 2)
        // ════════════════════════════════════════════════════════════════════════════

        private static HistoryException Malformed(long at, string what)
            => new(HistoryError.SnapshotMalformed,
                   $"the season's save cannot be decoded at byte {at.ToString(CultureInfo.InvariantCulture)}: {what}. " +
                   "Nothing was played; the career is unchanged.");

        /// <summary>The header, and the offset where the body starts. Refused as malformed when the magic line or
        /// the header itself cannot be read.</summary>
        public static (SeasonSnapshotHeader Header, int BodyAt) ReadHeader(byte[] bytes)
        {
            if (bytes.Length < Magic.Length || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                throw Malformed(0, "it does not begin with the season save's magic line");
            using var ms = new MemoryStream(bytes, Magic.Length, bytes.Length - Magic.Length, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            try
            {
                var h = new SeasonSnapshotHeader(
                    r.ReadString(), r.ReadString(), r.ReadString(), r.ReadInt64(), r.ReadString(), r.ReadInt64(),
                    new ProgressHeld(r.ReadInt64(), r.ReadInt64(), r.ReadInt64()));
                return (h, Magic.Length + (int)ms.Position);
            }
            catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
            {
                throw Malformed(Magic.Length + ms.Position, "its header is cut short or garbled");
            }
        }

        /// <summary>The graph the body holds, built in three stages (allocate, fill, rehash). Every failure —
        /// a truncation, a dangling id, an unknown type, a length past the end, bytes after the end — is
        /// <see cref="HistoryError.SnapshotMalformed"/> naming the byte.</summary>
        public static object ReadBody(byte[] bytes, int bodyAt)
        {
            using var ms = new MemoryStream(bytes, writable: false);
            ms.Position = bodyAt;
            using var r = new BinaryReader(ms, Encoding.UTF8);
            try
            {
                return new GraphReader(r, ms).Run();
            }
            catch (HistoryException) { throw; }
            catch (Exception ex) when (ex is EndOfStreamException or IOException or InvalidDataException or ArgumentException
                                           or InvalidCastException or FormatException or OverflowException
                                           or NotSupportedException or TargetInvocationException or MissingMethodException
                                           or InvalidOperationException or IndexOutOfRangeException
                                           or TypeLoadException or FileLoadException or BadImageFormatException
                                           or OutOfMemoryException)
            {
                throw Malformed(ms.Position, ex is EndOfStreamException ? "it ends before its last object is complete" : ex.Message);
            }
        }

        /// <summary>★ S127 — where a snapshot's sections and objects sit, for the checks (C6 lists the types met;
        /// C7 builds malformed saves at exact positions). Read by the same reader a resume uses.</summary>
        public sealed record SnapshotLayout(
            int BodyAt, List<(string Name, long At)> Types, long ObjectCountAt, List<long> TableAt, List<long> ContentAt, long End);

        public static SnapshotLayout Layout(byte[] bytes)
        {
            var (_, bodyAt) = ReadHeader(bytes);
            using var ms = new MemoryStream(bytes, writable: false);
            ms.Position = bodyAt;
            using var r = new BinaryReader(ms, Encoding.UTF8);
            var g = new GraphReader(r, ms) { Record = true };
            g.Run();
            return new SnapshotLayout(bodyAt, g.TypesSeen, g.ObjectCountAt, g.TableAt, g.ContentAt, ms.Length);
        }

        private sealed class GraphReader
        {
            public bool Record;
            public readonly List<(string Name, long At)> TypesSeen = new();
            public long ObjectCountAt;
            public readonly List<long> TableAt = new(), ContentAt = new();
            private readonly BinaryReader _r;
            private readonly MemoryStream _ms;
            private readonly Dictionary<Type, FieldInfo[]> _fields = new();
            private Type[] _types = Array.Empty<Type>();
            private object[] _objects = Array.Empty<object>();
            private Kind[] _kinds = Array.Empty<Kind>();
            private int[] _lists = Array.Empty<int>();   // a list's count, for its fill
            private readonly Dictionary<int, (int Count, List<(object? K, object? V)> Entries)> _pending = new();

            public GraphReader(BinaryReader r, MemoryStream ms) { _r = r; _ms = ms; }

            private long Remaining => _ms.Length - _ms.Position;

            private int Count(string what)
            {
                var at = _ms.Position;
                var n = _r.Read7BitEncodedInt();
                if (n < 0 || n > Remaining) throw Malformed(at, $"{what} of {n.ToString(CultureInfo.InvariantCulture)} runs past the end of the save");
                return n;
            }

            public object Run()
            {
                // ── The type table ──
                var typeCount = Count("a type table");
                _types = new Type[typeCount];
                for (var i = 0; i < typeCount; i++)
                {
                    var at = _ms.Position;
                    var name = _r.ReadString();
                    if (Record) TypesSeen.Add((name, at));
                    var t = Type.GetType(name, throwOnError: false);
                    if (t is null) throw Malformed(at, $"it names a type this build does not have ('{Short(name)}')");
                    if (Classify(t, out _) is not { } k || (k.Kind != Kind.Frozen && k.Stored != t))
                        throw Malformed(at, $"it names {t.Name}, a type a season save does not hold");
                    _types[i] = t;
                }

                // ── Stage 1: allocate ──
                ObjectCountAt = _ms.Position;
                var objectCount = Count("an object table");
                if (objectCount == 0) throw Malformed(_ms.Position, "it holds no objects");
                _objects = new object[objectCount];
                _kinds = new Kind[objectCount];
                _lists = new int[objectCount];
                for (var id = 0; id < objectCount; id++)
                {
                    var at = _ms.Position;
                    if (Record) TableAt.Add(at);
                    var ti = _r.Read7BitEncodedInt();
                    if (ti < 0 || ti >= _types.Length) throw Malformed(at, $"object {id} names type {ti}, past the type table");
                    var t = _types[ti];
                    var kind = Classify(t, out _)!.Value.Kind;
                    _kinds[id] = kind;
                    switch (kind)
                    {
                        case Kind.Class:
                            _objects[id] = RuntimeHelpers.GetUninitializedObject(t);
                            break;
                        case Kind.Array:
                        {
                            var rank = _r.Read7BitEncodedInt();
                            if (rank != t.GetArrayRank()) throw Malformed(at, $"object {id} is a rank-{rank} {t.Name}");
                            var lengths = new int[rank];
                            long total = 1;
                            for (var k = 0; k < rank; k++) { lengths[k] = Count("an array length"); total *= Math.Max(1, lengths[k]); }
                            if (total > Remaining + objectCount) throw Malformed(at, "an array runs past the end of the save");
                            _objects[id] = Array.CreateInstance(t.GetElementType()!, lengths);
                            break;
                        }
                        case Kind.List:
                            _lists[id] = Count("a list");
                            _objects[id] = Activator.CreateInstance(t, _lists[id])!;
                            break;
                        case Kind.Dictionary:
                        case Kind.HashSet:
                        {
                            var n = Count(kind == Kind.Dictionary ? "a dictionary" : "a set");
                            var code = _r.ReadByte();
                            var comparer = ComparerFor(code, t.GetGenericArguments()[0]);
                            _objects[id] = Activator.CreateInstance(t, n, comparer)!;
                            _pending[id] = (n, new List<(object?, object?)>(n));
                            break;
                        }
                        case Kind.Frozen:
                        {
                            var args = t.GetGenericArguments();
                            var n = Count("a frozen dictionary");
                            var source = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args), n)!;
                            for (var k = 0; k < n; k++)
                            {
                                var key = ReadValue(args[0]);
                                source.Add(key!, ReadValue(args[1]));
                            }
                            var freeze = typeof(FrozenDictionary).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                .Single(m => m.Name == nameof(FrozenDictionary.ToFrozenDictionary) && m.GetGenericArguments().Length == 2
                                             && m.GetParameters().Length == 2
                                             && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                                .MakeGenericMethod(args);
                            _objects[id] = freeze.Invoke(null, new object?[] { source, null })!;
                            break;
                        }
                    }
                }

                // ── Stage 2: fill ──
                for (var id = 0; id < objectCount; id++)
                {
                    if (Record) ContentAt.Add(_ms.Position);
                    var o = _objects[id];
                    var t = o.GetType();
                    switch (_kinds[id])
                    {
                        case Kind.Class:
                            foreach (var f in FieldsOf(t, _fields))
                            {
                                if (IsRebound(f)) continue;
                                f.SetValue(o, ReadValue(f.FieldType));
                            }
                            break;
                        case Kind.Array:
                        {
                            var a = (Array)o;
                            var e = t.GetElementType()!;
                            if (a.Rank == 1)
                                for (var i = 0; i < a.Length; i++) a.SetValue(ReadValue(e), i);
                            else
                            {
                                var idx = new int[a.Rank];
                                for (var n = 0; n < a.Length; n++)
                                {
                                    var rest = n;
                                    for (var k = a.Rank - 1; k >= 0; k--) { idx[k] = rest % a.GetLength(k); rest /= a.GetLength(k); }
                                    a.SetValue(ReadValue(e), idx);
                                }
                            }
                            break;
                        }
                        case Kind.List:
                        {
                            var list = (IList)o;
                            var e = t.GetGenericArguments()[0];
                            for (var i = 0; i < _lists[id]; i++) list.Add(ReadValue(e));
                            break;
                        }
                        case Kind.HashSet:
                        {
                            var e = t.GetGenericArguments()[0];
                            var (n, entries) = _pending[id];
                            for (var i = 0; i < n; i++) entries.Add((ReadValue(e), null));
                            break;
                        }
                        case Kind.Dictionary:
                        {
                            var args = t.GetGenericArguments();
                            var (n, entries) = _pending[id];
                            for (var i = 0; i < n; i++) { var k = ReadValue(args[0]); entries.Add((k, ReadValue(args[1]))); }
                            break;
                        }
                        case Kind.Frozen:
                            break;
                    }
                }
                if (_ms.Position != _ms.Length)
                    throw Malformed(_ms.Position, $"{(_ms.Length - _ms.Position).ToString(CultureInfo.InvariantCulture)} byte(s) follow its last object");

                // ── Stage 3: rehash — every field is in place, so every key's hash is its real one ──
                for (var id = 0; id < objectCount; id++)
                {
                    if (!_pending.TryGetValue(id, out var p)) continue;
                    var o = _objects[id];
                    if (_kinds[id] == Kind.Dictionary)
                    {
                        var d = (IDictionary)o;
                        foreach (var (k, v) in p.Entries) d.Add(k!, v);
                    }
                    else
                    {
                        var add = o.GetType().GetMethod("Add")!;
                        foreach (var (k, _) in p.Entries)
                            if (!(bool)add.Invoke(o, new[] { k })!)
                                throw Malformed(_ms.Position, $"object {id}, a set, holds one value twice");
                    }
                }
                return _objects[0];
            }

            private static string Short(string s) => s.Length <= 80 ? s : s[..80] + "…";

            private object? ReadValue(Type declared)
            {
                if (declared == typeof(string)) return _r.ReadBoolean() ? _r.ReadString() : null;
                if (declared.IsEnum) return Enum.ToObject(declared, ReadValue(Enum.GetUnderlyingType(declared))!);
                if (declared.IsPrimitive)
                {
                    if (declared == typeof(bool)) return _r.ReadBoolean();
                    if (declared == typeof(byte)) return _r.ReadByte();
                    if (declared == typeof(sbyte)) return _r.ReadSByte();
                    if (declared == typeof(short)) return _r.ReadInt16();
                    if (declared == typeof(ushort)) return _r.ReadUInt16();
                    if (declared == typeof(int)) return _r.ReadInt32();
                    if (declared == typeof(uint)) return _r.ReadUInt32();
                    if (declared == typeof(long)) return _r.ReadInt64();
                    if (declared == typeof(ulong)) return _r.ReadUInt64();
                    if (declared == typeof(char)) return (char)_r.ReadUInt16();
                    if (declared == typeof(float)) return BitConverter.Int32BitsToSingle(_r.ReadInt32());
                    if (declared == typeof(double)) return BitConverter.Int64BitsToDouble(_r.ReadInt64());
                    throw new InvalidDataException($"a {declared.Name} is not a value a season save holds");
                }
                if (declared == typeof(decimal)) return new decimal(new[] { _r.ReadInt32(), _r.ReadInt32(), _r.ReadInt32(), _r.ReadInt32() });
                if (declared == typeof(DateOnly))
                {
                    var at = _ms.Position;
                    var n = _r.ReadInt32();
                    if (n < DateOnly.MinValue.DayNumber || n > DateOnly.MaxValue.DayNumber) throw Malformed(at, "a date out of range");
                    return DateOnly.FromDayNumber(n);
                }
                if (Nullable.GetUnderlyingType(declared) is { } under) return _r.ReadBoolean() ? ReadValue(under) : null;
                if (declared.IsValueType)
                {
                    var box = RuntimeHelpers.GetUninitializedObject(declared);
                    foreach (var f in FieldsOf(declared, _fields)) f.SetValue(box, ReadValue(f.FieldType));
                    return box;
                }
                var at2 = _ms.Position;
                var tag = _r.Read7BitEncodedInt();
                if (tag == 0) return null;
                if (tag == 1) return _r.ReadString();
                var id = tag - 2;
                if (id < 0 || id >= _objects.Length) throw Malformed(at2, $"a reference to object {id.ToString(CultureInfo.InvariantCulture)}, past its object table (a dangling reference)");
                return _objects[id];
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        //  WHAT THE CALLER SUPPLIES AGAIN, AND WHAT BELONGS TO THE WORLD
        // ════════════════════════════════════════════════════════════════════════════

        /// <summary>The fields a resume rebinds rather than restores (K1): the caller's career store, world,
        /// settings path, verbosity and close seam, and the walk's two callbacks.</summary>
        public static bool IsRebound(FieldInfo f)
            => (f.DeclaringType == typeof(SeasonRun) && f.Name is "history" or "world" or "engineConfigPath" or "verbose" or "closeStepForTest")
               || (f.DeclaringType == typeof(SeasonCalendarRun) && f.Name is "_play" or "_monthDone");

        /// <summary>Every reference object reachable from <paramref name="root"/> (the world), strings and empty
        /// arrays aside (an empty array is shared freely and holds nothing).</summary>
        public static HashSet<object> Reachable(object root)
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var fields = new Dictionary<Type, FieldInfo[]>();
            var queue = new Queue<object>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var o = queue.Dequeue();
                var t = o.GetType();
                if (o is string || o is Delegate || IsLeaf(t)) continue;
                if (!t.IsValueType)
                {
                    if (o is Array { Length: 0 }) continue;
                    if (!seen.Add(o)) continue;
                }
                if (o is IDictionary d) { foreach (DictionaryEntry e in d) { queue.Enqueue(e.Key); if (e.Value is not null) queue.Enqueue(e.Value); } continue; }
                if (o is IEnumerable en && (t.IsArray || t.Namespace?.StartsWith("System", StringComparison.Ordinal) == true))
                { foreach (var e in en) if (e is not null) queue.Enqueue(e); continue; }
                if (t.Namespace?.StartsWith("System", StringComparison.Ordinal) == true && !IsValueTuple(t)) continue;
                foreach (var f in FieldsOf(t, fields))
                    if (!f.FieldType.IsPointer && f.GetValue(o) is { } v) queue.Enqueue(v);
            }
            return seen;
        }
    }
}
