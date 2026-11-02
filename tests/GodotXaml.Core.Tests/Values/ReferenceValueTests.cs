using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

// References and objects. ExtResource and SubResource take one id, a string or a legacy number, as the text resource
// loader reads them (scene/resources/resource_format_text.cpp:103-134) and its saver writes them (:1628-1650);
// Resource takes a path, a uid or both in either order (core/variant/variant_parser.cpp:1090-1187), and the writer
// puts the uid first (encode_resource_reference, :2002-2010). Object(...) is :995-1089, written by :2213-2235.
public sealed class ReferenceValueTests
{
    [Theory]
    [InlineData("ExtResource(\"1_abcde\")", GodotRefKind.External, "1_abcde")]
    [InlineData("SubResource(\"StyleBoxFlat_x2y3z\")", GodotRefKind.Sub, "StyleBoxFlat_x2y3z")]
    [InlineData("ExtResource( \"2\" )", GodotRefKind.External, "2")]
    public void ReadsAnIdString(string text, GodotRefKind kind, string id) =>
        Assert.Equal(new GodotResourceRef(kind, id), GodotValueReader.Read(text));

    // resource_format_text.cpp:111, 128: String id = token.value, the number's text.
    [Theory]
    [InlineData("ExtResource(3)", GodotRefKind.External, "3", "ExtResource(\"3\")")]
    [InlineData("SubResource(12)", GodotRefKind.Sub, "12", "SubResource(\"12\")")]
    public void ReadsALegacyNumberIdAsItsText(string text, GodotRefKind kind, string id, string written)
    {
        GodotValue value = GodotValueReader.Read(text);
        Assert.Equal(new GodotResourceRef(kind, id), value);
        Assert.Equal(written, Write(value));
    }

    [Theory]
    [InlineData("Resource(\"res://a.tres\")", null, "res://a.tres", "Resource(\"res://a.tres\")")]
    [InlineData("Resource(\"uid://abc\")", "uid://abc", null, "Resource(\"uid://abc\")")]
    [InlineData("Resource(\"uid://abc\", \"res://a.tres\")", "uid://abc", "res://a.tres", "Resource(\"uid://abc\", \"res://a.tres\")")]
    [InlineData("Resource(\"res://a.tres\", \"uid://abc\")", "uid://abc", "res://a.tres", "Resource(\"uid://abc\", \"res://a.tres\")")]
    public void ReadsAPathAUidOrBothInEitherOrder(string text, string? uid, string? path, string written)
    {
        GodotValue value = GodotValueReader.Read(text);
        Assert.Equal(new GodotPathRef(uid, path), value);
        Assert.Equal(written, Write(value));
    }

    [Theory]
    [InlineData("Object(RefCounted,\"script\":null)\n")]
    [InlineData("Object(Resource,\"resource_local_to_scene\":false,\"resource_name\":\"\",\"script\":null)\n")]
    [InlineData("Object(Node,\"a\":[1.0, Object(RefCounted,\"script\":null)\n],\"b\":{\n\"k\": 2\n})\n")]
    public void ReadsAndWritesObjects(string text) => Assert.Equal(text, Write(GodotValueReader.Read(text)));

    [Fact]
    public void ReadsAnObjectsClassAndPropertiesInOrder()
    {
        GodotValue value = GodotValueReader.Read("Object( Node , \"b\" : 1 , \"a\" : 2.5 , )");
        Assert.Equal(new GodotObject("Node", [new GodotProperty("b", new GodotInt(1)), new GodotProperty("a", new GodotFloat(2.5))]), value);
        Assert.Equal("Object(Node,\"b\":1,\"a\":2.5)\n", Write(value));
    }

    [Fact]
    public void WritesAnEmptyObject() => Assert.Equal("Object(Node,)\n", Write(new GodotObject("Node", [])));

    // Godot's own error texts.
    [Theory]
    [InlineData("Resource(\"uid://a\", \"uid://b\")", "Two uid:// paths in one Resource reference")]
    [InlineData("Resource(\"res://a\", \"res://b\")", "Two non-uid paths in one Resource reference")]
    [InlineData("Resource(\"res://a\", 1)", "Expected string in Resource reference")]
    [InlineData("Resource(1)", "Expected string as argument for Resource()")]
    [InlineData("Resource(\"res://a\"", "Expected ')'")]
    [InlineData("Resource", "Expected '('")]
    [InlineData("ExtResource()", "Expected number (old style sub-resource index) or String (ext-resource ID)")]
    [InlineData("SubResource(x)", "Expected number (old style sub-resource index) or string")]
    [InlineData("ExtResource(\"1\" \"2\")", "Expected ')'")]
    [InlineData("Object(1)", "Expected identifier with type of object")]
    [InlineData("Object(Node)", "Expected ',' after object type")]
    [InlineData("Object(Node,", "Expected property name as string")]
    [InlineData("Object(Node, \"a\":", "Expected value, got 'EOF'")]
    [InlineData("Object(Node, \"a\": \"x\"", "Expected '}' or ','")]
    [InlineData("Object(Node, 1: 2)", "Expected property name as string")]
    [InlineData("Object(Node, \"a\" 2)", "Expected ':'")]
    [InlineData("Object(Node, \"a\": 1 \"b\": 2)", "Expected '}' or ','")]
    [InlineData("Object(Node, \"a\": 1", "Unexpected EOF while parsing Object()")]
    public void RejectsWhatGodotRejects(string text, string reason)
    {
        GodotValueParseException error = Assert.Throws<GodotValueParseException>(() => GodotValueReader.Read(text));
        Assert.Equal(reason, error.Reason);
    }

    private static string Write(GodotValue value) => GodotValueWriter.Write(value, ValueWriteOptions.Default);
}
