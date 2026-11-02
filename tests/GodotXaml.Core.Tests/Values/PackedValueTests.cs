using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

// Packed arrays: _parse_byte_array, _parse_construct and the packed branches of parse_value
// (core/variant/variant_parser.cpp:552-675, 1411-1618), written by VariantWriter::write (:2402-2556).
public sealed class PackedValueTests
{
    // Texts Godot writes with compat on (var_to_str), which read and write back unchanged.
    [Theory]
    [InlineData("PackedByteArray()")]
    [InlineData("PackedByteArray(0, 1, 255)")]
    [InlineData("PackedInt32Array(-2147483648, 0, 2147483647)")]
    [InlineData("PackedInt64Array(-9223372036854775808, 9223372036854775807)")]
    [InlineData("PackedFloat32Array(0.1, 1, inf, inf_neg, nan)")]
    [InlineData("PackedFloat64Array(0.1, 0.3333333333333333, 1e+300)")]
    [InlineData("PackedStringArray()")]
    [InlineData("PackedStringArray(\"\", \"a\\n\\\"b\\\\\")")]
    [InlineData("PackedVector2Array(0, 0, 0, 1, 1, 1, 1, 0)")]
    [InlineData("PackedVector3Array(1, 2, 3, -0.5, 0, 1e-07)")]
    [InlineData("PackedVector4Array(1, 2, 3, 4)")]
    [InlineData("PackedColorArray(1, 0, 0, 1, 0.1, 0.2, 0.3, 0.4)")]
    public void ReadsAndWritesGodotText(string text) => Assert.Equal(text, Write(GodotValueReader.Read(text), compat: true));

    // Legacy names, base64, ragged and wrapping inputs Godot reads, written in its compat form.
    [Theory]
    [InlineData("PackedByteArray(\"AQID\")", "PackedByteArray(1, 2, 3)")]
    [InlineData("PackedByteArray(1, 2, 3)", "PackedByteArray(1, 2, 3)")]
    [InlineData("PackedByteArray(\"\")", "PackedByteArray()")]
    [InlineData("PackedByteArray(256, -1, 1.9)", "PackedByteArray(0, 255, 1)")]
    // Variant::_to_int<uint8_t> of a double is uint8_t(double), which x86-64 converts through a 32-bit cvttsd2si.
    [InlineData("PackedByteArray(2147483649.0)", "PackedByteArray(0)")]
    [InlineData("PackedByteArray(257.0)", "PackedByteArray(1)")]
    [InlineData("PackedByteArray(\"AQIDBA==\")", "PackedByteArray(1, 2, 3, 4)")]
    [InlineData("PoolByteArray(1, 2)", "PackedByteArray(1, 2)")]
    [InlineData("ByteArray()", "PackedByteArray()")]
    [InlineData("PackedIntArray(1, 2)", "PackedInt32Array(1, 2)")]
    [InlineData("PoolIntArray()", "PackedInt32Array()")]
    [InlineData("IntArray(3)", "PackedInt32Array(3)")]
    [InlineData("PackedInt32Array(4294967297, 1.9)", "PackedInt32Array(1, 1)")]
    [InlineData("PackedInt64Array(1.9, -1)", "PackedInt64Array(1, -1)")]
    [InlineData("PackedRealArray(0.1)", "PackedFloat32Array(0.1)")]
    [InlineData("PoolRealArray()", "PackedFloat32Array()")]
    [InlineData("FloatArray(2, -inf)", "PackedFloat32Array(2, inf_neg)")]
    [InlineData("PackedStringArray(\"a\",)", "PackedStringArray(\"a\")")]
    [InlineData("PoolStringArray(\"a\", \"b\")", "PackedStringArray(\"a\", \"b\")")]
    [InlineData("StringArray()", "PackedStringArray()")]
    [InlineData("PackedVector2Array(1, 2, 3)", "PackedVector2Array(1, 2)")]
    [InlineData("PoolVector2Array(1, 2)", "PackedVector2Array(1, 2)")]
    [InlineData("Vector2Array()", "PackedVector2Array()")]
    [InlineData("PackedVector3Array(1, 2, 3, 4)", "PackedVector3Array(1, 2, 3)")]
    [InlineData("PoolVector3Array(1, 2, 3)", "PackedVector3Array(1, 2, 3)")]
    [InlineData("Vector3Array()", "PackedVector3Array()")]
    [InlineData("PoolVector4Array(1, 2, 3, 4)", "PackedVector4Array(1, 2, 3, 4)")]
    [InlineData("Vector4Array()", "PackedVector4Array()")]
    [InlineData("PackedColorArray(1, 0, 0)", "PackedColorArray()")]
    [InlineData("PoolColorArray(1, 0, 0, 1)", "PackedColorArray(1, 0, 0, 1)")]
    [InlineData("ColorArray()", "PackedColorArray()")]
    public void WritesLooseTextAsGodotDoes(string input, string expected) =>
        Assert.Equal(expected, Write(GodotValueReader.Read(input), compat: true));

    // With compat off the saver writes bytes as base64 and an empty array with no argument (variant_parser.cpp:2411-2415).
    [Theory]
    [InlineData("", "PackedByteArray()")]
    [InlineData("FF", "PackedByteArray(\"/w==\")")]
    [InlineData("0001", "PackedByteArray(\"AAE=\")")]
    [InlineData("010203", "PackedByteArray(\"AQID\")")]
    public void WritesBytesAsBase64WithCompatOff(string hex, string expected)
    {
        var value = new GodotPackedBytes([.. Convert.FromHexString(hex)]);
        Assert.Equal(expected, Write(value, compat: false));
        Assert.Equal(value, GodotValueReader.Read(expected));
    }

    [Fact]
    public void ReadsBase64AndListAsTheSameBytes() =>
        Assert.Equal(GodotValueReader.Read("PackedByteArray(1, 2, 3)"), GodotValueReader.Read("PackedByteArray(\"AQID\")"));

    [Fact]
    public void RejectsComponentsThatAreNotAWholeNumberOfElements() =>
        Assert.Throws<ArgumentException>(() => new GodotPackedReals(GodotPackedKind.Vector3, [1, 2]));

    // Godot's own error texts.
    [Theory]
    [InlineData("PackedByteArray(\"!!!!\")", "Invalid base64-encoded string")]
    // CryptoCore::b64_decode takes no whitespace inside the string, which Convert.FromBase64String skips.
    [InlineData("PackedByteArray(\"AQID BA==\")", "Invalid base64-encoded string")]
    [InlineData("PackedByteArray(\"AQID	BA==\")", "Invalid base64-encoded string")]
    [InlineData("PackedByteArray(\"AQID\", 1)", "Expected ')' in constructor")]
    [InlineData("PackedByteArray(1,)", "Expected number in constructor")]
    [InlineData("PackedByteArray(1 2)", "Expected ',' or ')' in constructor")]
    [InlineData("PackedByteArray([])", "Expected base64 string, or list of numbers in constructor")]
    [InlineData("PackedByteArray", "Expected '(' in constructor")]
    [InlineData("PackedInt32Array(1,)", "Expected float in constructor")]
    [InlineData("PackedFloat32Array(\"1\")", "Expected float in constructor")]
    [InlineData("PackedStringArray(1)", "Expected string")]
    [InlineData("PackedStringArray(\"a\" \"b\")", "Expected ',' or ')'")]
    [InlineData("PackedStringArray", "Expected '('")]
    [InlineData("Vector2(1,)", "Expected float in constructor")]
    public void RejectsWhatGodotRejects(string text, string reason)
    {
        GodotValueParseException error = Assert.Throws<GodotValueParseException>(() => GodotValueReader.Read(text));
        Assert.Equal(reason, error.Reason);
    }

    private static string Write(GodotValue value, bool compat) => GodotValueWriter.Write(value, new ValueWriteOptions(compat));
}
