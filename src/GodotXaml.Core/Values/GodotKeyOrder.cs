using System.Collections.Immutable;
using System.Numerics;

namespace GodotXaml.Core.Values;

/// <summary>
/// The order Godot writes dictionary keys in, StringLikeVariantOrder (core/variant/variant.cpp:3413-3431), and the
/// key equality of its Dictionary (StringLikeVariantComparator, variant.cpp:3400-3411). Two String or StringName keys
/// compare by code point; any other pair by Variant type first, then by OP_LESS for the types that have one
/// (core/variant/variant_op.cpp:731-745). Two keys of a type without OP_LESS (Color, NodePath, ...) compare equal,
/// so a stable sort keeps them in source order; Godot's own order for them is not reproducible. Nor is its order for
/// Array keys holding StringNames: inside an Array, Variant::operator&lt; compares two StringNames with
/// StringName::operator&lt;, which compares interned pointers (core/string/string_name.h); they are compared by text here.
/// Object keys are compared by instance, as Variant::hash_compare does, so two equal objects are two keys.
/// </summary>
internal sealed class GodotKeyOrder : IComparer<GodotValue>, IEqualityComparer<GodotValue>
{
    // core/variant/variant.h:96-146, Variant::Type, for the records whose type does not hang on a kind.
    private static readonly Dictionary<Type, int> TypeRanks = new()
    {
        [typeof(GodotNull)] = 0,
        [typeof(GodotBool)] = 1,
        [typeof(GodotInt)] = 2,
        [typeof(GodotFloat)] = 3,
        [typeof(GodotString)] = 4,
        [typeof(GodotStringName)] = 21,
        [typeof(GodotNodePath)] = 22,
        [typeof(GodotRid)] = 23,
        [typeof(GodotObject)] = 24,
        [typeof(GodotResourceRef)] = 24,
        [typeof(GodotPathRef)] = 24,
        [typeof(GodotCallable)] = 25,
        [typeof(GodotSignal)] = 26,
        [typeof(GodotDictionary)] = 27,
        [typeof(GodotArray)] = 28,
        [typeof(GodotPackedBytes)] = 29,
        [typeof(GodotPackedInts)] = 30,
        [typeof(GodotPackedLongs)] = 31,
        [typeof(GodotPackedFloats)] = 32,
        [typeof(GodotPackedDoubles)] = 33,
        [typeof(GodotPackedStrings)] = 34,
    };

    private static readonly Dictionary<GodotRealKind, int> RealRanks = new()
    {
        [GodotRealKind.Vector2] = 5,
        [GodotRealKind.Rect2] = 7,
        [GodotRealKind.Vector3] = 9,
        [GodotRealKind.Transform2D] = 11,
        [GodotRealKind.Vector4] = 12,
        [GodotRealKind.Plane] = 14,
        [GodotRealKind.Quaternion] = 15,
        [GodotRealKind.Aabb] = 16,
        [GodotRealKind.Basis] = 17,
        [GodotRealKind.Transform3D] = 18,
        [GodotRealKind.Projection] = 19,
        [GodotRealKind.Color] = 20,
    };

    private static readonly Dictionary<GodotIntKind, int> IntRanks = new()
    {
        [GodotIntKind.Vector2I] = 6,
        [GodotIntKind.Rect2I] = 8,
        [GodotIntKind.Vector3I] = 10,
        [GodotIntKind.Vector4I] = 13,
    };

    private static readonly Dictionary<GodotPackedKind, int> PackedRanks = new()
    {
        [GodotPackedKind.Vector2] = 35,
        [GodotPackedKind.Vector3] = 36,
        [GodotPackedKind.Color] = 37,
        [GodotPackedKind.Vector4] = 38,
    };

    // variant_op.cpp:731-745: OP_LESS between two values of one type, for the types that register it.
    private static readonly Dictionary<Type, Func<GodotValue, GodotValue, bool>> SameTypeLess = new()
    {
        [typeof(GodotBool)] = (a, b) => !((GodotBool)a).Value && ((GodotBool)b).Value,
        [typeof(GodotInt)] = (a, b) => ((GodotInt)a).Value < ((GodotInt)b).Value,
        [typeof(GodotFloat)] = (a, b) => ((GodotFloat)a).Value < ((GodotFloat)b).Value,
        [typeof(GodotString)] = (a, b) => CompareCodePoints(((GodotString)a).Value, ((GodotString)b).Value) < 0,
        [typeof(GodotStringName)] = (a, b) => CompareCodePoints(((GodotStringName)a).Value, ((GodotStringName)b).Value) < 0,
        [typeof(GodotReal)] = (a, b) =>
            ((GodotReal)a).Kind is GodotRealKind.Vector2 or GodotRealKind.Vector3 or GodotRealKind.Vector4
            && ComponentsLess(((GodotReal)a).Components, ((GodotReal)b).Components),
        [typeof(GodotInts)] = (a, b) =>
            ((GodotInts)a).Kind != GodotIntKind.Rect2I && ComponentsLess(((GodotInts)a).Components, ((GodotInts)b).Components),
        [typeof(GodotRid)] = (a, b) => (((GodotRid)a).Id ?? 0) < (((GodotRid)b).Id ?? 0),
        [typeof(GodotArray)] = (a, b) => ArrayLess(((GodotArray)a).Items, ((GodotArray)b).Items),
    };

    private GodotKeyOrder() { }

    /// <summary>The one comparer.</summary>
    public static GodotKeyOrder Instance { get; } = new();

    /// <inheritdoc />
    public int Compare(GodotValue? x, GodotValue? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (Less(x, y))
        {
            return -1;
        }

        return Less(y, x) ? 1 : 0;
    }

    /// <summary>Whether two keys are one key of a Godot Dictionary.</summary>
    /// <param name="x">One key.</param>
    /// <param name="y">The other key.</param>
    /// <returns>
    /// True when a String and a StringName have the same text, or the values are equal; an Object is only ever the
    /// same key as itself, since Variant::hash_compare compares objects by instance.
    /// </returns>
    public bool Equals(GodotValue? x, GodotValue? y)
    {
        if (x is GodotObject || y is GodotObject)
        {
            return ReferenceEquals(x, y);
        }

        return StringLike(x) is { } left && StringLike(y) is { } right ? string.Equals(left, right, StringComparison.Ordinal) : object.Equals(x, y);
    }

    /// <inheritdoc />
    public int GetHashCode(GodotValue obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj is GodotObject)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        return StringLike(obj) is { } text ? StringComparer.Ordinal.GetHashCode(text) : obj.GetHashCode();
    }

    /// <summary>The entries with each key once, at its first place and with its last value (variant_parser.cpp:1743).</summary>
    /// <param name="entries">The entries.</param>
    /// <returns>The merged entries; the same array when no key repeats.</returns>
    public static ImmutableArray<GodotEntry> Merge(ImmutableArray<GodotEntry> entries)
    {
        var places = new Dictionary<GodotValue, int>(Instance);
        var merged = new List<GodotEntry>(entries.Length);
        foreach (GodotEntry entry in entries)
        {
            if (places.TryGetValue(entry.Key, out int place))
            {
                merged[place] = merged[place] with { Value = entry.Value };
                continue;
            }

            places.Add(entry.Key, merged.Count);
            merged.Add(entry);
        }

        return merged.Count == entries.Length ? entries : [.. merged];
    }

    /// <summary>
    /// Whether two sets of merged entries hold the same keys with equal values, in any order, as Dictionary's
    /// recursive_equal compares them (core/variant/dictionary.cpp:263-285).
    /// </summary>
    /// <param name="left">One set of entries, each key once.</param>
    /// <param name="right">The other set of entries, each key once.</param>
    /// <returns>True when they hold the same entries.</returns>
    public static bool SameEntries(ImmutableArray<GodotEntry> left, ImmutableArray<GodotEntry> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var values = new Dictionary<GodotValue, GodotValue>(Instance);
        foreach (GodotEntry entry in right)
        {
            values[entry.Key] = entry.Value;
        }

        return left.All(entry => values.TryGetValue(entry.Key, out GodotValue? value) && value.Equals(entry.Value));
    }

    /// <summary>A hash of entries that does not hang on their order, matching <see cref="SameEntries"/>.</summary>
    /// <param name="entries">The entries.</param>
    /// <returns>The hash.</returns>
    public static int HashOfEntries(ImmutableArray<GodotEntry> entries)
    {
        int hash = entries.Length;
        foreach (GodotEntry entry in entries)
        {
            hash ^= HashCode.Combine(Instance.GetHashCode(entry.Key), entry.Value);
        }

        return hash;
    }

    // str_compare (core/string/ustring.h:108-121) compares code points. UTF-16 order differs from it only where a
    // surrogate (0xD800-0xDFFF) meets a unit at or above 0xE000, so moving the surrogates above that range gives code
    // point order unit by unit.
    internal static int CompareCodePoints(string left, string right)
    {
        int length = Math.Min(left.Length, right.Length);
        for (int i = 0; i < length; i++)
        {
            if (left[i] != right[i])
            {
                return CodePointRank(left[i]) - CodePointRank(right[i]);
            }
        }

        return left.Length - right.Length;
    }

    private static int CodePointRank(char unit) =>
        unit switch
        {
            >= '\uD800' and <= '\uDFFF' => unit + 0x2000,
            >= '\uE000' => unit - 0x800,
            _ => unit,
        };

    // StringLikeVariantOrder::compare: two string-like keys by text, any other pair by Variant::operator<.
    private static bool Less(GodotValue left, GodotValue right) =>
        StringLike(left) is { } l && StringLike(right) is { } r ? CompareCodePoints(l, r) < 0 : VariantLess(left, right);

    // variant.cpp:868-876, Variant::operator<: by type when the types differ, else OP_LESS, false where there is none.
    private static bool VariantLess(GodotValue left, GodotValue right)
    {
        int leftRank = Rank(left);
        int rightRank = Rank(right);
        if (leftRank != rightRank)
        {
            return leftRank < rightRank;
        }

        return SameTypeLess.TryGetValue(left.GetType(), out Func<GodotValue, GodotValue, bool>? less) && less(left, right);
    }

    private static int Rank(GodotValue value) =>
        value switch
        {
            GodotReal real => RealRanks[real.Kind],
            GodotInts ints => IntRanks[ints.Kind],
            GodotPackedReals packed => PackedRanks[packed.Kind],
            _ => TypeRanks[value.GetType()],
        };

    // core/math/vector2.h:166 and its siblings: the first unequal component decides; equal vectors are not less.
    private static bool ComponentsLess<T>(ImmutableArray<T> left, ImmutableArray<T> right)
        where T : INumber<T>
    {
        for (int i = 0; i < left.Length - 1; i++)
        {
            if (left[i] != right[i])
            {
                return left[i] < right[i];
            }
        }

        return left[^1] < right[^1];
    }

    // core/variant/array.cpp:164-179, Array::operator<: element by element with Variant::operator<, then by length.
    private static bool ArrayLess(ImmutableArray<GodotValue> left, ImmutableArray<GodotValue> right)
    {
        int length = Math.Min(left.Length, right.Length);
        for (int i = 0; i < length; i++)
        {
            if (VariantLess(left[i], right[i]))
            {
                return true;
            }

            if (VariantLess(right[i], left[i]))
            {
                return false;
            }
        }

        return left.Length < right.Length;
    }

    private static string? StringLike(GodotValue? value) =>
        value switch
        {
            GodotString text => text.Value,
            GodotStringName name => name.Value,
            _ => null,
        };
}
