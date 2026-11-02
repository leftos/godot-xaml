using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// GXAML-7: a forward conversion keeps the x:Resources keys of the document it replaces, so a save changes only what
// changed (DECISIONS.md, "Canonical formatting on save").
public sealed class StableKeyTests
{
    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new();

    [Fact]
    public void WithoutAPreviousDocumentKeysAreMadeFromScratch()
    {
        ConversionResult<XscnDocument> forward = Forward(
            Scene(
                Section("StyleBoxFlat", "1", "bg_color = Color(1, 0, 0, 1)"),
                Section("StyleBoxFlat", "2", "bg_color = Color(0, 1, 0, 1)"),
                UsedTwice("1", "2")
            ),
            previous: null
        );

        AssertKeys(forward, "StyleBoxFlat1", "StyleBoxFlat2");
    }

    [Fact]
    public void AnEqualResourceKeepsItsKey()
    {
        string text = Scene(Section("StyleBoxFlat", "1", "bg_color = Color(1, 0, 0, 1)"), UsedTwice("1"));

        ConversionResult<XscnDocument> forward = Forward(text, Previous(text, "Panel"));

        AssertKeys(forward, "Panel");
    }

    [Fact]
    public void TheSameResourceNameKeepsItsKey()
    {
        string previousText = Scene(Section("StyleBoxFlat", "1", "resource_name = \"Fancy\"\nbg_color = Color(1, 0, 0, 1)"), UsedTwice("1"));
        string text = Scene(Section("StyleBoxFlat", "1", "resource_name = \"Fancy\"\nbg_color = Color(0, 0, 1, 1)"), UsedTwice("1"));

        ConversionResult<XscnDocument> forward = Forward(text, Previous(previousText, "Hero"));

        AssertKeys(forward, "Hero");
    }

    [Fact]
    public void ANewResourceGetsTheNextFreeCounter()
    {
        string previousText = Scene(Section("StyleBoxFlat", "1", "bg_color = Color(1, 0, 0, 1)"), UsedTwice("1"));
        string text = Scene(
            Section("StyleBoxFlat", "1", "bg_color = Color(1, 0, 0, 1)"),
            Section("StyleBoxFlat", "2", "bg_color = Color(0, 1, 0, 1)"),
            UsedTwice("1", "2")
        );

        // From scratch the red one would be StyleBoxFlat1; kept as StyleBoxFlat2, it leaves 1 free for the new green one.
        ConversionResult<XscnDocument> forward = Forward(text, Previous(previousText, "StyleBoxFlat2"));

        AssertKeys(forward, "StyleBoxFlat2", "StyleBoxFlat1");
    }

    [Fact]
    public void TwoMatchesForOneEntryTakeItOnce()
    {
        string previousText = Scene(Section("StyleBoxFlat", "1", "bg_color = Color(1, 0, 0, 1)"), UsedTwice("1"));
        string text = Scene(
            Section("StyleBoxFlat", "1", "bg_color = Color(1, 0, 0, 1)"),
            Section("StyleBoxFlat", "2", "bg_color = Color(1, 0, 0, 1)"),
            UsedTwice("1", "2")
        );

        ConversionResult<XscnDocument> forward = Forward(text, Previous(previousText, "Panel"));

        AssertKeys(forward, "Panel", "StyleBoxFlat1");
    }

    [Fact]
    public void ADifferentTypeNeverMatches()
    {
        string previousText = Scene(Section("StyleBoxEmpty", "1"), UsedTwice("1"));
        string text = Scene(Section("StyleBoxEmpty", "1"), Section("StyleBoxFlat", "2"), UsedTwice("1", "2"));

        ConversionResult<XscnDocument> forward = Forward(text, Previous(previousText, "Empty"));

        AssertKeys(forward, "Empty", "StyleBoxFlat1");
    }

    [Fact]
    public void AnInlineResourceGetsNoKeyWhateverThePreviousDocument()
    {
        string previousText = Scene(Section("StyleBoxFlat", "1", "bg_color = Color(1, 0, 0, 1)"), UsedTwice("1"));
        string text = Scene(
            Section("StyleBoxFlat", "1", "bg_color = Color(1, 0, 0, 1)"),
            "[node name=\"Root\" type=\"PanelContainer\"]\ntheme_override_styles/panel = SubResource(\"1\")"
        );

        ConversionResult<XscnDocument> forward = Forward(text, Previous(previousText, "Panel"));

        Assert.Empty(forward.Issues);
        Assert.Empty(forward.Document.Resources);
    }

    [Fact]
    public void AReferenceToAnotherSubResourceComparesAsAnyReference()
    {
        const string nodes = """
            [node name="Root" type="Node"]
            metadata/first = SubResource("1")

            [node name="Second" type="Node" parent="."]
            metadata/second = SubResource("1")

            [node name="A" type="TextureRect" parent="."]
            texture = SubResource("2")

            [node name="B" type="TextureRect" parent="."]
            texture = SubResource("2")
            """;
        string previousText = Scene(
            Section("Gradient", "1", "colors = PackedColorArray(1, 0, 0, 1, 0, 0, 1, 1)"),
            Section("GradientTexture2D", "2", "gradient = SubResource(\"1\")"),
            nodes
        );
        string text = Scene(
            Section("Gradient", "1", "colors = PackedColorArray(0, 1, 0, 1, 0, 0, 0, 1)"),
            Section("GradientTexture2D", "2", "gradient = SubResource(\"1\")"),
            nodes
        );

        ConversionResult<XscnDocument> forward = Forward(text, Previous(previousText, "Gradient1", "Outer"));

        AssertKeys(forward, "Gradient1", "Outer");
    }

    [Fact]
    public void AFileComparesByItsPath()
    {
        string text = Scene(
            "[ext_resource type=\"Texture2D\" path=\"res://a.png\" id=\"1\"]",
            Section("StyleBoxTexture", "2", "texture = ExtResource(\"1\")"),
            UsedTwice("2")
        );

        ConversionResult<XscnDocument> forward = Forward(text, Previous(text, "Panel"));

        AssertKeys(forward, "Panel");
    }

    [Fact]
    public void ATakenKeyIsNotGivenAgainByResourceName()
    {
        string previousText = Scene(Section("StyleBoxFlat", "1", "resource_name = \"Fancy\"\nbg_color = Color(1, 0, 0, 1)"), UsedTwice("1"));
        string text = Scene(
            Section("StyleBoxFlat", "1", "resource_name = \"Fancy\"\nbg_color = Color(0, 0, 1, 1)"),
            Section("StyleBoxFlat", "2", "resource_name = \"Hero\""),
            UsedTwice("1", "2")
        );

        ConversionResult<XscnDocument> forward = Forward(text, Previous(previousText, "Hero"));

        AssertKeys(forward, "Hero", "StyleBoxFlat1");
    }

    // The document a save replaces: a first conversion, whose entries then take the keys the earlier save gave them.
    private static XscnDocument Previous(string text, params string[] keys)
    {
        ConversionResult<XscnDocument> converted = TscnToXscn.Convert(TscnReader.Read(text), Metadata, Options);
        Assert.Empty(converted.Issues);
        Assert.Equal(keys.Length, converted.Document.Resources.Length);
        return converted.Document with { Resources = [.. converted.Document.Resources.Select((entry, index) => entry with { Key = keys[index] })] };
    }

    private static ConversionResult<XscnDocument> Forward(string text, XscnDocument? previous) =>
        TscnToXscn.Convert(TscnReader.Read(text), Metadata, Options with { Previous = previous });

    private static void AssertKeys(ConversionResult<XscnDocument> forward, params string[] keys)
    {
        Assert.Empty(forward.Issues);
        Assert.Equal(keys, KeysOf(forward.Document));
    }

    private static string?[] KeysOf(XscnDocument document) => [.. document.Resources.Select(entry => entry.Key)];

    private static string Scene(params string[] sections) => "[gd_scene format=3]\n\n" + string.Join("\n\n", sections);

    private static string Section(string type, string id, string? properties = null) =>
        properties is null ? $"[sub_resource type=\"{type}\" id=\"{id}\"]" : $"[sub_resource type=\"{type}\" id=\"{id}\"]\n{properties}";

    private static readonly string[] RootNode = ["[node name=\"Root\" type=\"Node\"]"];

    // A root node and two PanelContainers per sub_resource id: used twice each as a bare property value, a sub_resource
    // goes into x:Resources rather than inline.
    private static string UsedTwice(params string[] ids) =>
        string.Join("\n\n", RootNode.Concat(ids.SelectMany((id, index) => new[] { Panel(index, "A", id), Panel(index, "B", id) })));

    private static string Panel(int group, string letter, string id) =>
        $"[node name=\"G{group}{letter}\" type=\"PanelContainer\" parent=\".\"]\ntheme_override_styles/panel = SubResource(\"{id}\")";
}
