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

    [Fact]
    public void AnEditedResourceWithNoNameKeepsItsKey()
    {
        XscnDocument previous = Parse(BoxScene);
        string saved = Edited(previous, "bg_color = Color(1, 0, 0, 1)", "bg_color = Color(0, 0, 1, 1)");

        ConversionResult<XscnDocument> forward = Forward(saved, previous);

        AssertKeys(forward, "Box");
        Assert.Equal(2, Occurrences(XscnWriter.Write(forward.Document, Metadata), "ThemeOverrideStyles.panel=\"{Box}\""));
    }

    [Fact]
    public void TwoEditedResourcesOfOneTypeEachKeepTheirKey()
    {
        XscnDocument previous = Parse(TwoBoxScene);
        string saved = Edited(previous, "bg_color = Color(1, 0, 0, 1)", "bg_color = Color(0, 0, 1, 1)")
            .Replace("bg_color = Color(0, 1, 0, 1)", "bg_color = Color(1, 0, 1, 1)", StringComparison.Ordinal);

        ConversionResult<XscnDocument> forward = Forward(saved, previous);

        AssertKeys(forward, "A", "B");
        string written = XscnWriter.Write(forward.Document, Metadata);
        Assert.Contains("x:Key=\"A\" BgColor=\"0, 0, 1, 1\"", written, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"B\" BgColor=\"1, 0, 1, 1\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEditedResourceKeepsTheCommentsOfItsEntry()
    {
        XscnDocument previous = Parse(CommentedBoxScene);
        string saved = Edited(previous, "bg_color = Color(1, 0, 0, 1)", "bg_color = Color(0, 0, 1, 1)");

        ConversionResult<XscnDocument> forward = Forward(saved, previous);

        AssertKeys(forward, "Box");
        Assert.DoesNotContain(forward.Issues, issue => issue.Code == ConversionCodes.CommentDropped);
        Assert.Contains("<!-- about the box -->", XscnWriter.Write(forward.Document, Metadata), StringComparison.Ordinal);
    }

    [Fact]
    public void AResourceWhoseTypeChangedUnderItsIdGetsANewKey()
    {
        XscnDocument previous = Parse(BoxScene.Replace(" BgColor=\"1, 0, 0, 1\"", "", StringComparison.Ordinal));
        string saved = Edited(previous, "type=\"StyleBoxFlat\"", "type=\"StyleBoxEmpty\"");

        ConversionResult<XscnDocument> forward = Forward(saved, previous);

        AssertKeys(forward, "StyleBoxEmpty1");
    }

    [Fact]
    public void ADuplicateOfAKeyedResourceGetsANewKey()
    {
        XscnDocument previous = Parse(BoxScene);
        string id = Assert.Single(SavedDocument(previous).SubResources).Id;
        string text = Scene(
            Section("StyleBoxFlat", "2", "bg_color = Color(1, 0, 0, 1)"),
            Section("StyleBoxFlat", id, "bg_color = Color(1, 0, 0, 1)"),
            RootNode[0],
            Panel(1, "A", "2"),
            Panel(1, "B", "2"),
            Panel(0, "A", id),
            Panel(0, "B", id)
        );

        ConversionResult<XscnDocument> forward = Forward(text, previous);

        AssertKeys(forward, "StyleBoxFlat1", "Box");
    }

    [Fact]
    public void ADuplicateDoesNotTakeTheKeyOfAnEntryThatWentInline()
    {
        XscnDocument previous = Parse(BoxScene);
        string id = Assert.Single(SavedDocument(previous).SubResources).Id;
        string text = Scene(
            Section("StyleBoxFlat", id, "bg_color = Color(1, 0, 0, 1)"),
            Section("StyleBoxFlat", "2", "bg_color = Color(1, 0, 0, 1)"),
            RootNode[0],
            Panel(0, "A", id),
            Panel(1, "A", "2"),
            Panel(1, "B", "2")
        );

        ConversionResult<XscnDocument> forward = Forward(text, previous);

        AssertKeys(forward, "StyleBoxFlat1");
        Assert.DoesNotContain(forward.Document.Resources, entry => entry.Key == "Box");
    }

    [Fact]
    public void ANewResourceInADeletedOnesPlaceFallsBackToTheMatch()
    {
        XscnDocument previous = Parse(BoxScene);
        string text = Scene(Section("StyleBoxFlat", "2", "bg_color = Color(1, 0, 0, 1)"), UsedTwice("2"));

        ConversionResult<XscnDocument> forward = Forward(text, previous);

        AssertKeys(forward, "Box");
    }

    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    // The previous document of a save that edits its keyed entry: used twice, the entry is in x:Resources, not inline.
    private static readonly string BoxScene = $$"""
        <Node2D {{X}} Name="Root">
          <x:Resources>
            <StyleBoxFlat x:Key="Box" BgColor="1, 0, 0, 1" />
          </x:Resources>
          <PanelContainer Name="A" ThemeOverrideStyles.panel="{Box}" />
          <PanelContainer Name="B" ThemeOverrideStyles.panel="{Box}" />
        </Node2D>
        """;

    private static readonly string CommentedBoxScene = $$"""
        <Node2D {{X}} Name="Root">
          <x:Resources>
            <!-- about the box -->
            <StyleBoxFlat x:Key="Box" BgColor="1, 0, 0, 1" />
          </x:Resources>
          <PanelContainer Name="A" ThemeOverrideStyles.panel="{Box}" />
          <PanelContainer Name="B" ThemeOverrideStyles.panel="{Box}" />
        </Node2D>
        """;

    private static readonly string TwoBoxScene = $$"""
        <Node2D {{X}} Name="Root">
          <x:Resources>
            <StyleBoxFlat x:Key="A" BgColor="1, 0, 0, 1" />
            <StyleBoxFlat x:Key="B" BgColor="0, 1, 0, 1" />
          </x:Resources>
          <PanelContainer Name="AA" ThemeOverrideStyles.panel="{A}" />
          <PanelContainer Name="AB" ThemeOverrideStyles.panel="{A}" />
          <PanelContainer Name="BA" ThemeOverrideStyles.panel="{B}" />
          <PanelContainer Name="BB" ThemeOverrideStyles.panel="{B}" />
        </Node2D>
        """;

    // The .tscn a save writes for the previous document, with one text replaced as an edit of the resource would.
    private static string Edited(XscnDocument previous, string from, string to) =>
        TscnWriter.Write(SavedDocument(previous)).Replace(from, to, StringComparison.Ordinal);

    private static TscnDocument SavedDocument(XscnDocument previous)
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(previous, Metadata, Options);
        Assert.Empty(back.Issues);
        return back.Document;
    }

    private static XscnDocument Parse(string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata);
        Assert.Empty(parsed.Diagnostics);
        return parsed.Document;
    }

    private static int Occurrences(string text, string value) =>
        (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;

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
