using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

// The order Godot writes dictionary keys in: StringLikeVariantOrder (core/variant/variant.cpp:3413-3431), which
// compares String and StringName keys by code point (str_compare, core/string/ustring.h:108-121) and any other pair
// with Variant::operator< (variant.cpp:868-876): by type first, then by OP_LESS where the type has one
// (core/variant/variant_op.cpp:731-745).
public sealed class GodotKeyOrderTests
{
    // U+1F600 is a surrogate pair in UTF-16, whose lead unit (0xD83D) is below U+E000: UTF-16 order puts it first,
    // code point order last.
    [Fact]
    public void OrdersStringsByCodePointNotByUtf16Unit()
    {
        GodotValue[] keys = [S("\U0001F600"), S(""), S("z"), S("é")];
        Assert.True(string.CompareOrdinal("\U0001F600", "") < 0);
        Assert.Equal([S("z"), S("é"), S(""), S("\U0001F600")], Sorted(keys));
    }

    [Fact]
    public void OrdersUppercaseBeforeLowercaseAndPunctuationByCodePoint()
    {
        GodotValue[] keys = [S("{"), S("a"), S("["), S("B"), S("("), S("'"), S("\"")];
        Assert.Equal([S("\""), S("'"), S("("), S("B"), S("["), S("a"), S("{")], Sorted(keys));
    }

    [Fact]
    public void OrdersAShorterPrefixFirst()
    {
        GodotValue[] keys = [S("ab"), S("a"), S("")];
        Assert.Equal([S(""), S("a"), S("ab")], Sorted(keys));
    }

    // INT (2) before FLOAT (3) before STRING (4) by type, whatever the numbers: 10 comes before 0.5.
    [Fact]
    public void OrdersIntBeforeFloatBeforeStringByType()
    {
        GodotValue[] keys = [S("a"), new GodotFloat(0.5), new GodotInt(10), new GodotFloat(-3), new GodotInt(-1)];
        Assert.Equal([new GodotInt(-1), new GodotInt(10), new GodotFloat(-3), new GodotFloat(0.5), S("a")], Sorted(keys));
    }

    [Fact]
    public void OrdersStringAndStringNameKeysTogetherByText()
    {
        GodotValue[] keys = [N("b"), S("c"), N("A"), S("a")];
        Assert.Equal([N("A"), S("a"), N("b"), S("c")], Sorted(keys));
    }

    [Fact]
    public void OrdersBoolKeysFalseFirst()
    {
        GodotValue[] keys = [new GodotBool(true), new GodotBool(false)];
        Assert.Equal([new GodotBool(false), new GodotBool(true)], Sorted(keys));
    }

    // Vector2::operator< (core/math/vector2.h:166): x first, y when the x are equal.
    [Fact]
    public void OrdersVectorKeysComponentByComponent()
    {
        GodotValue[] keys = [V2(1, 2), V2(1, -1), V2(0, 5)];
        Assert.Equal([V2(0, 5), V2(1, -1), V2(1, 2)], Sorted(keys));
    }

    // Color has no OP_LESS, so two Color keys compare equal; Godot's unstable sort leaves them in an order its hash map
    // decides, and the writer keeps them in the order they were given.
    [Fact]
    public void KeepsColorKeysInSourceOrder()
    {
        GodotValue red = new GodotReal(GodotRealKind.Color, [1, 0, 0, 1]);
        GodotValue black = new GodotReal(GodotRealKind.Color, [0, 0, 0, 1]);
        Assert.Equal([red, black], Sorted([red, black]));
        Assert.Equal([black, red], Sorted([black, red]));
    }

    private static GodotString S(string text) => new(text);

    private static GodotStringName N(string text) => new(text);

    private static GodotReal V2(float x, float y) => new(GodotRealKind.Vector2, [x, y]);

    private static GodotValue[] Sorted(GodotValue[] keys) => [.. keys.Order(GodotKeyOrder.Instance)];
}
