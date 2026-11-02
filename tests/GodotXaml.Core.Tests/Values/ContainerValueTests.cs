using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

// Arrays and dictionaries, plain and typed: _parse_array, _parse_dictionary and the Array[ / Dictionary[ branches of
// parse_value (core/variant/variant_parser.cpp:1188-1410, 1644-1748), written by VariantWriter::write (:2237-2400).
public sealed class ContainerValueTests
{
    // Texts Godot writes, which read and write back unchanged.
    [Theory]
    [InlineData("[]")]
    [InlineData("[1, 1.0, \"a\", &\"b\", null]")]
    [InlineData("[[], [1, [2.0]], {}]")]
    [InlineData("[PackedStringArray(), []]")]
    [InlineData("{}")]
    [InlineData("{\n\"a\": 1\n}")]
    [InlineData("{\n\"a\": [{\n\"x\": 5.0\n}],\n\"b\": {}\n}")]
    [InlineData("{\n-1: 5,\n1: 4,\n0.5: 6,\n&\"n\": 2,\n\"s\": 1\n}")]
    [InlineData("Array[int]([1, 2])")]
    [InlineData("Array[RID]([])")]
    [InlineData("Array[Material]([])")]
    [InlineData("Array[String]([\"' '\", \"\\\" \\\"\"])")]
    [InlineData("Dictionary[String, int]({\n\"a\": 1\n})")]
    [InlineData("Dictionary[String, Variant]({})")]
    [InlineData("Dictionary[Variant, String]({})")]
    [InlineData("Dictionary[StringName, Texture2D]({})")]
    public void ReadsAndWritesGodotText(string text) => Assert.Equal(text, RoundTrip(text));

    // Loose forms Godot reads, written in its own form.
    [Theory]
    [InlineData("[1,]", "[1]")]
    [InlineData("[1, [2,],]", "[1, [2]]")]
    [InlineData("[\n1, ; comment\n2\n]", "[1, 2]")]
    [InlineData("{\"a\": 1,}", "{\n\"a\": 1\n}")]
    [InlineData("{\"b\": 1, \"a\": 2}", "{\n\"a\": 2,\n\"b\": 1\n}")]
    [InlineData("{\"a\": 1, \"b\": 2, \"a\": 3}", "{\n\"a\": 3,\n\"b\": 2\n}")]
    [InlineData("{\"a\": 1, &\"a\": 2}", "{\n\"a\": 2\n}")]
    [InlineData("{1: \"int\", 1.0: \"float\"}", "{\n1: \"int\",\n1.0: \"float\"\n}")]
    [InlineData("Array[ int ]( [ 1 , 2 ] )", "Array[int]([1, 2])")]
    [InlineData("Array[Variant]([1])", "[1]")]
    [InlineData("Array[Nil]([])", "[]")]
    [InlineData("Dictionary[Variant, Variant]({\"a\": 1})", "{\n\"a\": 1\n}")]
    public void WritesLooseTextAsGodotDoes(string input, string expected) => Assert.Equal(expected, RoundTrip(input));

    // variant_parser.cpp:1358-1364: the bare class name Resource is followed by ']' rather than '(', and Godot takes it
    // as the class; any other reference name is a script reference.
    [Fact]
    public void ReadsBareResourceAsTheClassAndExtResourceAsAScript()
    {
        var resource = (GodotArray)GodotValueReader.Read("Array[Resource]([])");
        Assert.Equal(new GodotTypeRef("Resource"), resource.ElementType);

        var script = (GodotArray)GodotValueReader.Read("Array[ExtResource(\"1_ab\")]([])");
        Assert.Equal(new GodotTypeRef(new GodotResourceRef(GodotRefKind.External, "1_ab")), script.ElementType);
        Assert.Equal("Array[ExtResource(\"1_ab\")]([])", Write(script));
    }

    // variant_parser.cpp:1221-1227, 1267-1273: the rescue works for a key type before ',' and a value type before ']'.
    [Theory]
    [InlineData("Dictionary[Resource, int]({})")]
    [InlineData("Dictionary[int, Resource]({})")]
    [InlineData("Dictionary[SubResource(\"Script_x\"), Resource(\"uid://a\", \"res://b.gd\")]({})")]
    public void ReadsReferenceTypesOfADictionary(string text) => Assert.Equal(text, RoundTrip(text));

    [Fact]
    public void ReadsTypedDictionarySides()
    {
        var dictionary = (GodotDictionary)GodotValueReader.Read("Dictionary[String, Variant]({})");
        Assert.Equal(new GodotTypeRef("String"), dictionary.KeyType);
        Assert.Null(dictionary.ValueType);
    }

    // _parse_dictionary keeps a repeated key's first place and its last value (variant_parser.cpp:1743).
    [Fact]
    public void KeepsARepeatedKeyAtItsFirstPlaceWithItsLastValue()
    {
        var dictionary = (GodotDictionary)GodotValueReader.Read("{\"b\": 1, \"a\": 2, &\"b\": 3}");
        Assert.Equal(
            [new GodotEntry(new GodotString("b"), new GodotInt(3)), new GodotEntry(new GodotString("a"), new GodotInt(2))],
            dictionary.Entries
        );
    }

    // A float element takes the top-level ".0" (variant_parser.cpp:2023-2031); a Vector2 component does not.
    [Fact]
    public void WritesFloatElementsWithTheirPoint()
    {
        var array = new GodotArray([new GodotFloat(1), new GodotReal(GodotRealKind.Vector2, [1, 0])], null);
        Assert.Equal("[1.0, Vector2(1, 0)]", Write(array));
    }

    // Godot's parser has no depth limit; an unbounded recursion would overflow the stack, which .NET cannot catch.
    [Fact]
    public void RejectsNestingDeeperThan512Levels()
    {
        GodotValueParseException error = Assert.Throws<GodotValueParseException>(() => GodotValueReader.Read(new string('[', 100_000)));
        Assert.Equal("Nesting is too deep (more than 512 levels)", error.Reason);
    }

    [Fact]
    public void Reads512NestedArrays()
    {
        string text = new string('[', 512) + new string(']', 512);
        Assert.Equal(text, RoundTrip(text));
    }

    [Fact]
    public void RejectsNestingThroughDictionariesAndObjects()
    {
        string text = string.Concat(Enumerable.Repeat("{1: Object(Node,\"a\":", 300));
        GodotValueParseException error = Assert.Throws<GodotValueParseException>(() => GodotValueReader.Read(text));
        Assert.Equal("Nesting is too deep (more than 512 levels)", error.Reason);
    }

    // Godot compares OBJECT keys by instance (Variant::hash_compare), so two Object(...) keys are two entries.
    [Fact]
    public void KeepsEqualObjectKeysAsTwoEntries()
    {
        var dictionary = (GodotDictionary)GodotValueReader.Read("{ Object(RefCounted,\"a\":1): 1, Object(RefCounted,\"a\":1): 2 }");
        Assert.Equal(2, dictionary.Entries.Length);
    }

    // Godot's own error texts.
    [Theory]
    [InlineData("[1 2]", "Expected ','")]
    // Godot checks is_eof() only at the start of an element, and the stream reaches its end only when a token was read
    // up to it (a number or an identifier reads one character past itself); otherwise parse_value or the separator
    // check meets TK_EOF (variant_parser.cpp:40-57, 1644-1748, 1031-1089).
    [InlineData("[1", "Unexpected EOF while parsing array")]
    [InlineData("[1, ", "Expected value, got 'EOF'")]
    [InlineData("[1,", "Expected value, got 'EOF'")]
    [InlineData("[", "Expected value, got 'EOF'")]
    [InlineData("[1 ", "Expected ','")]
    [InlineData("[\"a\"", "Expected ','")]
    [InlineData("{\"a\": 1,\n", "Expected value, got 'EOF'")]
    [InlineData("{\"a\"", "Expected ':'")]
    [InlineData("{\"a\":", "Expected value, got 'EOF'")]
    [InlineData("{\"a\": \"x\"", "Expected '}' or ','")]
    [InlineData("{\"a\": true", "Unexpected EOF while parsing dictionary")]
    [InlineData("[,]", "Expected value, got '',''")]
    [InlineData("{\"a\" 1}", "Expected ':'")]
    [InlineData("{\"a\": 1 \"b\": 2}", "Expected '}' or ','")]
    [InlineData("{\"a\": 1", "Unexpected EOF while parsing dictionary")]
    [InlineData("Array[int]", "Expected '('")]
    [InlineData("Array(int)", "Expected '['")]
    [InlineData("Array[1]([])", "Expected type identifier")]
    [InlineData("Array[int]({})", "Expected '['")]
    [InlineData("Array[int]([] x", "Expected ')'")]
    [InlineData("Array[ExtResource]([])", "Expected '('")]
    [InlineData("Dictionary[int]({})", "Expected ',' after key type")]
    [InlineData("Dictionary[1, int]({})", "Expected type identifier for key")]
    [InlineData("Dictionary[int, 1]({})", "Expected type identifier for value")]
    [InlineData("Dictionary[int, int]([])", "Expected '{'")]
    public void RejectsWhatGodotRejects(string text, string reason)
    {
        GodotValueParseException error = Assert.Throws<GodotValueParseException>(() => GodotValueReader.Read(text));
        Assert.Equal(reason, error.Reason);
    }

    private static string RoundTrip(string text) => Write(GodotValueReader.Read(text));

    private static string Write(GodotValue value) => GodotValueWriter.Write(value, ValueWriteOptions.Default);
}
