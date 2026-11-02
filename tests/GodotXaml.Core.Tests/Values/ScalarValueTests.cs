using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

public sealed class ScalarValueTests
{
    // Texts Godot writes (variant_parser.cpp:2012-2173), which read and write back unchanged.
    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("-42")]
    [InlineData("9223372036854775807")]
    [InlineData("1.0")]
    [InlineData("0.1")]
    [InlineData("1e+20")]
    [InlineData("inf")]
    [InlineData("inf_neg")]
    [InlineData("nan")]
    [InlineData("\"\"")]
    [InlineData("\"a\\\\b\\\"c\nd'\"")]
    [InlineData("&\"\"")]
    [InlineData("&\"a\\nb\\'c\\\\\"")]
    [InlineData("NodePath(\"Child/Grandchild:position:x\")")]
    [InlineData("Vector2(0.1, -2.5)")]
    [InlineData("Vector3(1, 2.25, -1e-07)")]
    [InlineData("Vector4(1e+20, 0.3, 0.33333334, 0)")]
    [InlineData("Rect2(1.5, 2, 640, 480)")]
    [InlineData("Plane(0, 1, 0, 0.5)")]
    [InlineData("Quaternion(0, 0.7071068, 0, 0.7071068)")]
    [InlineData("AABB(-1, -1, -1, 2, 2, 2)")]
    [InlineData("Transform2D(1, 0, 0, 1, 10, 20)")]
    [InlineData("Basis(1, 0, 0, 0, 1, 0, 0, 0, 1)")]
    [InlineData("Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 2, 3)")]
    [InlineData("Projection(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)")]
    [InlineData("Color(1, 0.5, 0.25, 0.1)")]
    [InlineData("Vector2(inf, inf_neg)")]
    [InlineData("Vector2i(1, -2)")]
    [InlineData("Vector3i(2147483647, -2147483648, 0)")]
    [InlineData("Vector4i(1, 2, 3, 4)")]
    [InlineData("Rect2i(0, 0, 1920, 1080)")]
    [InlineData("RID()")]
    [InlineData("RID(12345)")]
    [InlineData("Signal()")]
    [InlineData("Callable()")]
    public void ReadsAndWritesGodotText(string text) => Assert.Equal(text, RoundTrip(text));

    // Legacy and loose forms Godot reads (variant_parser.cpp:263-265, 700-708, 813-894; color.cpp:331-370), written
    // in their 4.7.2 form.
    [Theory]
    [InlineData("nil", "null")]
    [InlineData("-inf", "inf_neg")]
    [InlineData("1.", "1.0")]
    [InlineData("1e5", "100000.0")]
    [InlineData("-0.0", "0.0")]
    [InlineData("-0", "0")]
    [InlineData("@\"legacy\"", "&\"legacy\"")]
    [InlineData("Matrix32(1, 0, 0, 1, 5, 6)", "Transform2D(1, 0, 0, 1, 5, 6)")]
    [InlineData("Quat(0, 0, 0, 1)", "Quaternion(0, 0, 0, 1)")]
    [InlineData("Rect3(0, 0, 0, 1, 1, 1)", "AABB(0, 0, 0, 1, 1, 1)")]
    [InlineData("Matrix3(1, 0, 0, 0, 1, 0, 0, 0, 1)", "Basis(1, 0, 0, 0, 1, 0, 0, 0, 1)")]
    [InlineData("Transform(1, 0, 0, 0, 1, 0, 0, 0, 1, 7, 8, 9)", "Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 7, 8, 9)")]
    [InlineData("#f80 ", "Color(1, 0.53333336, 0, 1)")]
    [InlineData("#f808 ", "Color(1, 0.53333336, 0, 0.53333336)")]
    [InlineData("#ff8000\n", "Color(1, 0.5019608, 0, 1)")]
    [InlineData("#ff800080 ", "Color(1, 0.5019608, 0, 0.5019608)")]
    [InlineData("Vector2( 1 ,\n2 ) ; comment", "Vector2(1, 2)")]
    [InlineData("Vector2(0.1, 0.2)", "Vector2(0.1, 0.2)")]
    [InlineData("Vector2(inf, -inf)", "Vector2(inf, inf_neg)")]
    [InlineData("Vector3(nan, inf_neg, 0)", "Vector3(nan, inf_neg, 0)")]
    [InlineData("Vector2i(1.7, -2.9)", "Vector2i(1, -2)")]
    [InlineData("RID( )", "RID()")]
    [InlineData("RID(0)", "RID()")]
    [InlineData("NodePath(\"a\\tb\")", "NodePath(\"a\\tb\")")]
    public void WritesLooseTextInItsCanonicalForm(string text, string expected) => Assert.Equal(expected, RoundTrip(text));

    [Fact]
    public void ReadsTheModel()
    {
        Assert.Equal(GodotNull.Instance, GodotValueReader.Read("null"));
        Assert.Equal(new GodotBool(true), GodotValueReader.Read("true"));
        Assert.Equal(new GodotInt(-42), GodotValueReader.Read("-42"));
        Assert.Equal(new GodotFloat(0.5), GodotValueReader.Read("0.5"));
        Assert.Equal(new GodotString("a\nb"), GodotValueReader.Read("\"a\nb\""));
        Assert.Equal(new GodotStringName("n"), GodotValueReader.Read("&\"n\""));
        Assert.Equal(new GodotNodePath(".."), GodotValueReader.Read("NodePath(\"..\")"));
        Assert.Equal(new GodotReal(GodotRealKind.Vector2, [0.1f, 2f]), GodotValueReader.Read("Vector2(0.1, 2)"));
        Assert.Equal(new GodotInts(GodotIntKind.Rect2I, [1, 2, 3, 4]), GodotValueReader.Read("Rect2i(1, 2, 3, 4)"));
        Assert.Equal(new GodotRid(null), GodotValueReader.Read("RID()"));
        Assert.Equal(new GodotRid(7), GodotValueReader.Read("RID(7)"));
        Assert.Equal(GodotSignal.Instance, GodotValueReader.Read("Signal()"));
        Assert.Equal(GodotCallable.Instance, GodotValueReader.Read("Callable()"));
    }

    // ustring.cpp:202-206: a NUL code point becomes U+FFFD; an escaped character above U+FFFF is taken whole.
    [Fact]
    public void ReadsEscapedNulAndAstralCharacters()
    {
        Assert.Equal(new GodotString("a�b"), GodotValueReader.Read("\"a\\u0000b\""));
        Assert.Equal(new GodotString("\U0001F600"), GodotValueReader.Read("\"\\\U0001F600\""));
    }

    // variant_parser.cpp:957: RID::from_uint64 of a number token; a float token's double becomes an int64 first, so a
    // negative id wraps, and the writer prints the id through itos, as a signed int64 (variant_parser.cpp:2158-2165).
    [Fact]
    public void ReadsARidFromAFloatAndWritesItsIdAsASignedInt()
    {
        Assert.Equal(new GodotRid(18446744073709551611UL), GodotValueReader.Read("RID(-5.0)"));
        Assert.Equal("RID(-5)", Write(GodotValueReader.Read("RID(-5.0)")));
        Assert.Equal(new GodotRid(18446744073709551611UL), GodotValueReader.Read("RID(-5)"));
    }

    [Fact]
    public void TreatsAnIdOfZeroAsTheEmptyRid() => Assert.Equal(new GodotRid(null), new GodotRid(0));

    [Fact]
    public void ComparesComponentRecordsByValue()
    {
        GodotValue first = GodotValueReader.Read("Vector3(1, 2, 3)");
        GodotValue second = GodotValueReader.Read("Vector3(1.0, 2, 3e0)");
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, GodotValueReader.Read("Vector3(1, 2, 4)"));
        Assert.NotEqual<GodotValue>(new GodotReal(GodotRealKind.Vector4, [1, 2, 3, 4]), new GodotReal(GodotRealKind.Quaternion, [1, 2, 3, 4]));
    }

    [Fact]
    public void RejectsAComponentCountTheKindDoesNotTake() => Assert.Throws<ArgumentException>(() => new GodotReal(GodotRealKind.Vector3, [1, 2]));

    // variant_parser.cpp:1989-1993: outside compat, negative infinity is "-inf".
    [Fact]
    public void WritesMinusInfOutsideCompat()
    {
        var options = new ValueWriteOptions(Compat: false);
        Assert.Equal("-inf", GodotValueWriter.Write(new GodotFloat(double.NegativeInfinity), options));
        Assert.Equal("Vector2(-inf, 1)", GodotValueWriter.Write(GodotValueReader.Read("Vector2(inf_neg, 1)"), options));
    }

    // ustring.cpp:4474-4496: a String escapes only '\' and '"'; a StringName and a NodePath escape the C set.
    [Fact]
    public void EscapesEachStringKindAsGodotDoes()
    {
        const string Text = "a\nb\t'c\"\\\a\v\f\b\r";
        Assert.Equal("\"a\nb\t'c\\\"\\\\\a\v\f\b\r\"", Write(new GodotString(Text)));
        Assert.Equal("&\"a\\nb\\t\\'c\\\"\\\\\\a\\v\\f\\b\\r\"", Write(new GodotStringName(Text)));
        Assert.Equal("NodePath(\"a\\nb\\t\\'c\\\"\\\\\\a\\v\\f\\b\\r\")", Write(new GodotNodePath(Text)));
    }

    [Theory]
    [InlineData("", "Expected value, got 'EOF'")]
    [InlineData("  ; only a comment", "Expected value, got 'EOF'")]
    [InlineData("#f80", "Expected value, got 'EOF'")]
    // variant_parser.cpp:130-148, 1639: tk_name quotes punctuation and the message quotes it again.
    [InlineData("[1]", "Expected value, got ''[''")]
    [InlineData("{}", "Expected value, got ''{''")]
    [InlineData(")", "Expected value, got '')''")]
    [InlineData("Array[int]([])", "Unexpected identifier 'Array'")]
    [InlineData("PackedByteArray()", "Unexpected identifier 'PackedByteArray'")]
    [InlineData("ExtResource(\"1\")", "Unexpected identifier 'ExtResource'")]
    [InlineData("Object(Node)", "Unexpected identifier 'Object'")]
    [InlineData("-nan", "Unexpected identifier '-nan'")]
    [InlineData("Vector2", "Expected '(' in constructor")]
    [InlineData("Vector2(1,)", "Expected float in constructor")]
    [InlineData("Vector2(1 2)", "Expected ',' or ')' in constructor")]
    [InlineData("Vector2(\"1\", 2)", "Expected float in constructor")]
    [InlineData("Vector2(1, 2, 3)", "Expected 2 arguments for constructor")]
    [InlineData("Transform3D()", "Expected 12 arguments for constructor")]
    [InlineData("NodePath", "Expected '('")]
    [InlineData("NodePath(1)", "Expected string as argument for NodePath()")]
    [InlineData("NodePath(\"a\"", "Expected ')'")]
    [InlineData("RID(\"x\")", "Expected number as argument or ')'")]
    [InlineData("RID(1, 2)", "Expected ')'")]
    [InlineData("Signal(1)", "Expected ')'")]
    [InlineData("Callable", "Expected '('")]
    [InlineData("1 2", "Expected end of text, got 'number'")]
    [InlineData("true false", "Expected end of text, got 'identifier'")]
    public void RejectsWhatGodotRejects(string text, string reason) =>
        Assert.Equal(reason, Assert.Throws<GodotValueParseException>(() => GodotValueReader.Read(text)).Reason);

    [Fact]
    public void AllowsTrailingWhitespaceAndComments() => Assert.Equal(new GodotInt(5), GodotValueReader.Read("5 \n ; done\n"));

    [Fact]
    public void ReadsOneValueAtAPositionAndKeepsTheLine()
    {
        const string Text = "a = Vector2(1,\n2)\nb = 3";
        int position = 4;
        int line = 1;
        Assert.Equal(new GodotReal(GodotRealKind.Vector2, [1, 2]), GodotValueReader.ReadAt(Text, ref position, ref line));
        Assert.Equal((17, 2), (position, line));
    }

    private static string RoundTrip(string text) => Write(GodotValueReader.Read(text));

    private static string Write(GodotValue value) => GodotValueWriter.Write(value, ValueWriteOptions.Default);
}
