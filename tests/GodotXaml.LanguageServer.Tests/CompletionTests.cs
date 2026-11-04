using System.Text.Json;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer.Tests;

public sealed class CompletionTests
{
    private const string FormatNamespace = "https://github.com/leftos/godot-xaml/format/1";

    private static readonly GodotMetadata Metadata = GodotMetadata.LoadBundled("4.7");

    private static readonly string SceneUri = new Uri(RepoRoot.PathOf("tests", "fixtures", "xscn", "Completion.xscn")).AbsoluteUri;

    private static readonly string ResourceUri = new Uri(RepoRoot.PathOf("tests", "fixtures", "xscn", "Completion.xres")).AbsoluteUri;

    [Fact]
    public async Task AnAttributeNameOffersTheClassesPropertiesAndSignals()
    {
        GodotClassInfo button = Metadata.FindClassByCSharpName("Button")!;
        XscnPropertyNames names = new(Metadata);
        List<string> properties =
        [
            .. Metadata
                .PropertiesOf(button)
                .Where(p => p.IsStored && !p.Name.Contains('/'))
                .Select(p => names.AttributeName(button, p.Name))
                .OfType<string>(),
        ];
        IEnumerable<string> signals = Metadata
            .SignalsOf(button)
            .Select(s => s.CSharpName!)
            .Where(s => Metadata.FindPropertyByCSharpName(button, s) is null);
        List<string> expected =
        [
            .. properties
                .Concat(signals)
                .Where(n => n.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];

        JsonElement result = await CompleteAsync(Root("<Button Mouse|"));

        Assert.Equal(expected, StartingWith(Labels(result), "Mouse"));
        Assert.Contains("MouseFilter", expected);
        Assert.Contains("MouseDefaultCursorShape", expected);
        JsonElement filter = Item(result, "MouseFilter");
        Assert.Equal(10, filter.GetProperty("kind").GetInt32());
        Assert.Equal("int", filter.GetProperty("detail").GetString());
        JsonElement entered = Item(result, "MouseEntered");
        Assert.Equal(23, entered.GetProperty("kind").GetInt32());
        Assert.Equal("signal", entered.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AButtonsMouseNamesAreTheseSix()
    {
        JsonElement result = await CompleteAsync(Root("<Button Mouse|"));

        Assert.Equal(
            ["MouseBehaviorRecursive", "MouseDefaultCursorShape", "MouseEntered", "MouseExited", "MouseFilter", "MouseForcePassScrollEvents"],
            StartingWith(Labels(result), "Mouse")
        );
    }

    // The client filters by what is typed, fuzzily; the server sends every name the context takes.
    [Fact]
    public async Task EveryNameOfTheContextIsSentWhateverIsTyped()
    {
        JsonElement result = await CompleteAsync(Root("<Button Mouse|"));

        List<string> labels = Labels(result);
        Assert.Contains("Text", labels);
        Assert.Contains("Pressed", labels);
        Assert.Contains("MouseFilter", labels);
        Assert.Equal([.. labels.Order(StringComparer.OrdinalIgnoreCase)], labels);
        Assert.False(result.GetProperty("isIncomplete").GetBoolean());
    }

    [Fact]
    public async Task AnAttributeAlreadyOnTheElementIsNotOfferedAgain()
    {
        JsonElement result = await CompleteAsync(Root("<Button MouseFilter=\"Stop\" Mouse|"));

        List<string> labels = Labels(result);
        Assert.DoesNotContain("MouseFilter", labels);
        Assert.Contains("MouseDefaultCursorShape", labels);
    }

    [Fact]
    public async Task AQuotedGreaterThanDoesNotEndTheStartTag()
    {
        JsonElement result = await CompleteAsync(Root("<Label Text=\"1 > 0\" MouseFilter=\"Stop\" Mouse|"));

        List<string> labels = Labels(result);
        Assert.DoesNotContain("MouseFilter", labels);
        Assert.DoesNotContain("Text", labels);
        Assert.Contains("MouseDefaultCursorShape", labels);
    }

    [Fact]
    public async Task ATagNameInANodeOffersNodeClassesOnly()
    {
        JsonElement result = await CompleteAsync(Root("<Butt|"));

        List<string> labels = Labels(result);
        Assert.Contains("Button", labels);
        Assert.DoesNotContain("ButtonGroup", labels);
        JsonElement button = Item(result, "Button");
        Assert.Equal(7, button.GetProperty("kind").GetInt32());
        Assert.Equal("BaseButton", button.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ANodesPropertyElementsAreItsResourceProperties()
    {
        JsonElement result = await CompleteAsync(Root("<Button Name=\"B\">\n    <Button.|\n  </Button>"));

        List<string> labels = Labels(result);
        Assert.Contains("Button.Theme", labels);
        Assert.DoesNotContain("Button.Text", labels);
    }

    [Fact]
    public async Task APropertyElementOffersTheClassesOfItsPropertysType()
    {
        JsonElement result = await CompleteAsync(Root("<Button Name=\"B\">\n    <Button.Theme><|\n    </Button.Theme>\n  </Button>"));

        List<string> labels = Labels(result);
        Assert.Contains("Theme", labels);
        Assert.DoesNotContain(labels, l => l.Contains("StyleBox", StringComparison.Ordinal));
    }

    // A hint of two classes offers the classes of both.
    [Fact]
    public async Task AHintOfTwoClassesOffersBoth()
    {
        JsonElement result = await CompleteAsync(Root("<Sprite2D Name=\"S\">\n    <Sprite2D.Material><|\n    </Sprite2D.Material>\n  </Sprite2D>"));

        List<string> labels = Labels(result);
        Assert.Contains("CanvasItemMaterial", labels);
        Assert.Contains("ShaderMaterial", labels);
    }

    // A hint's '-' entries leave those classes out.
    [Fact]
    public async Task AHintsExclusionsAreLeftOut()
    {
        JsonElement result = await CompleteAsync(Root("<Decal Name=\"D\">\n    <Decal.TextureAlbedo><|\n    </Decal.TextureAlbedo>\n  </Decal>"));

        List<string> labels = Labels(result);
        Assert.Contains("ImageTexture", labels);
        Assert.DoesNotContain("AnimatedTexture", labels);
        Assert.DoesNotContain("AtlasTexture", labels);
    }

    // An unknown class in a resource file is a resource: it holds no class element, only x:Prop and, on the root, the tables.
    [Fact]
    public async Task AnUnknownClassInAResourceFileTakesNoNodes()
    {
        JsonElement result = await CompleteAsync($"<MyStats xmlns:x=\"{FormatNamespace}\">\n  <|\n</MyStats>\n", ResourceUri);

        Assert.Equal(["x:Prop", "x:Resources", "x:Uids"], Labels(result));
        Assert.DoesNotContain(result.GetProperty("items").EnumerateArray(), i => i.GetProperty("kind").GetInt32() == 7);
    }

    [Fact]
    public async Task TheRootsChildrenTakeTheTablesAndTheNodesFormatElements()
    {
        JsonElement result = await CompleteAsync(Root("<x:|"));

        Assert.Equal(["x:Connect", "x:Instance", "x:Override", "x:Prop", "x:Resources", "x:Uids"], StartingWith(Labels(result), "x:"));
        Assert.Equal(14, Item(result, "x:Resources").GetProperty("kind").GetInt32());
    }

    [Fact]
    public async Task ANestedNodeTakesNoTables()
    {
        JsonElement result = await CompleteAsync(Root("<Button Name=\"B\">\n    <x:|\n  </Button>"));

        List<string> labels = Labels(result);
        Assert.DoesNotContain("x:Resources", labels);
        Assert.DoesNotContain("x:Uids", labels);
        Assert.DoesNotContain("x:Uid", labels);
        Assert.Contains("x:Connect", labels);
    }

    [Fact]
    public async Task TheUidTableTakesOnlyUids()
    {
        JsonElement result = await CompleteAsync(Root("<x:Uids>\n    <|\n  </x:Uids>"));

        Assert.Equal(["x:Uid"], Labels(result));
    }

    [Fact]
    public async Task AResourceTakesProp()
    {
        JsonElement result = await CompleteAsync(
            Root("<Button Name=\"B\">\n    <Button.Theme>\n      <Theme>\n        <x:|\n      </Theme>\n    </Button.Theme>\n  </Button>")
        );

        List<string> labels = Labels(result);
        Assert.Contains("x:Prop", labels);
        Assert.DoesNotContain("x:Connect", labels);
        Assert.DoesNotContain("Button", labels);
    }

    [Fact]
    public async Task AThemeOverridePrefixOffersThemeOverrideNames()
    {
        JsonElement result = await CompleteAsync(Root("<Panel ThemeOverride|"));

        List<string> labels = Labels(result);
        Assert.Contains("ThemeOverrideStyles.panel", labels);
        Assert.Equal(14, Item(result, "Name").GetProperty("kind").GetInt32());
    }

    [Fact]
    public async Task TheReplacementRangeCoversTheTypedPrefix()
    {
        string source = Root("<Button Mouse|");
        int cursor = source.IndexOf('|', StringComparison.Ordinal);
        string text = source.Remove(cursor, 1);
        LspRange typed = new(PositionOf(text, cursor - "Mouse".Length), PositionOf(text, cursor));

        JsonElement result = await CompleteAsync(source);

        Assert.False(result.GetProperty("isIncomplete").GetBoolean());
        Assert.NotEmpty(result.GetProperty("items").EnumerateArray());
        foreach (JsonElement item in result.GetProperty("items").EnumerateArray())
        {
            JsonElement edit = item.GetProperty("textEdit");
            Assert.Equal(typed, RangeOf(edit.GetProperty("range")));
            Assert.Equal(item.GetProperty("label").GetString(), edit.GetProperty("newText").GetString());
        }
    }

    [Theory]
    [InlineData("<!-- a no|te -->")]
    [InlineData("<Label Name=\"L\">hel|lo</Label>")]
    [InlineData("<Label Name=\"L\">hello</La|bel>")]
    [InlineData("<Button MouseFilter=\"St|op\" />")]
    public async Task NothingIsOfferedOutsideNames(string inner)
    {
        JsonElement result = await CompleteAsync(Root(inner));

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
    }

    // A scene whose root holds the inner text, the cursor marked '|'.
    private static string Root(string inner) => $"<Control xmlns:x=\"{FormatNamespace}\" Name=\"Root\">\n  {inner}\n</Control>\n";

    // Opens the source without its marker and asks for completions at the marker.
    private static async Task<JsonElement> CompleteAsync(string source, string? uri = null)
    {
        uri ??= SceneUri;
        int cursor = source.IndexOf('|', StringComparison.Ordinal);
        Assert.True(cursor >= 0, "no cursor marker");
        string text = source.Remove(cursor, 1);
        await using ServerHarness server = new();
        await server.InitializeAsync();
        await server.OpenAsync(uri, 1, text);
        await server.NextAsync(server.Published, "the didOpen publish");
        LspPosition position = PositionOf(text, cursor);

        JsonElement result = await server.RequestObjectAsync(
            "textDocument/completion",
            JsonSerializer.SerializeToElement(
                new { textDocument = new { uri }, position = new { line = position.Line, character = position.Character } }
            )
        );

        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        return result;
    }

    private static List<string> Labels(JsonElement result)
    {
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        return [.. result.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString()!)];
    }

    private static List<string> StartingWith(List<string> labels, string prefix) =>
        [.. labels.Where(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];

    private static JsonElement Item(JsonElement result, string label) =>
        result.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("label").GetString() == label);

    private static LspPosition PositionOf(string text, int offset)
    {
        LinePosition position = new LineMap(text).ZeroBased(offset);
        return new LspPosition(position.Line, position.Column);
    }

    private static LspRange RangeOf(JsonElement range) => new(PointOf(range.GetProperty("start")), PointOf(range.GetProperty("end")));

    private static LspPosition PointOf(JsonElement position) =>
        new(position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
}
