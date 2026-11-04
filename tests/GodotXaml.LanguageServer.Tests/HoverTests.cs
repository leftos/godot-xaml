using System.Text.Json;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer.Tests;

public sealed class HoverTests
{
    private const string FormatLink = "godot-xaml format attribute: https://github.com/leftos/godot-xaml/blob/main/docs/format/README.md";

    private const string Scene = """
        <Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
          <x:Resources>
            <StyleBoxFlat x:Key="Panel" />
          </x:Resources>
          <!-- a note -->
          <Button Name="Ok" MouseFilter="Ignore" SizeFlagsHorizontal="Expand, Fill" Pressed="OnPressed" Toggled="OnToggled"
            x:Groups="buttons" ThemeOverrideStyles.normal="{Panel}" foo:Bar="1" />
          <foo:Thing />
          <Bogus Name="B" />
        </Control>
        """;

    // The edges of each hoverable token: the hovered character is the one under the mouse.
    private const string Edges = """
        <Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
          <x:Resources>
            <StyleBoxFlat x:Key="Panel" />
          </x:Resources>
          <Button MouseFilter="Stop" Name="Ok" ThemeOverrideStyles.normal="{Panel}" />
        </Control>
        """;

    private const string Typed = """
        <Node3D xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
          <Decal Name="D" TextureNormal="res://n.png" />
          <CsgBox3D Name="Box" Material="res://m.tres" />
          <Area2D Name="Area" InputEvent="OnInput" />
        </Node3D>
        """;

    private static readonly ClassDocs Docs = ClassDocs.LoadBundled("4.7") ?? throw new InvalidOperationException("no 4.7 docs");

    private static readonly string Uri = new Uri(RepoRoot.PathOf("tests", "fixtures", "xscn", "Hover.xscn")).AbsoluteUri;

    [Fact]
    public async Task AClassTagShowsTheClassItsParentAndItsDescription() =>
        await AssertHoverAsync(
            "<Button",
            1,
            "Button",
            Markdown("class Button : BaseButton", Docs.Class("Button")!.Brief!, Docs.Class("Button")!.Description!)
        );

    [Fact]
    public async Task AnEnumPropertyShowsItsTypeDefaultValuesAndDescription() =>
        await AssertHoverAsync(
            "MouseFilter=",
            2,
            "MouseFilter",
            Markdown("int MouseFilter", "Default: 0", "Values: Stop, Pass, Ignore", Member("Control", "mouse_filter"))
        );

    [Fact]
    public async Task AFlagsPropertyListsItsFlags() =>
        await AssertHoverAsync(
            "SizeFlagsHorizontal=",
            1,
            "SizeFlagsHorizontal",
            Markdown(
                "int SizeFlagsHorizontal",
                "Default: 1",
                "Flags: ShrinkBegin, Fill, Expand, ExpandFill, ShrinkCenter, ShrinkEnd",
                Member("Control", "size_flags_horizontal")
            )
        );

    [Fact]
    public async Task ASignalWithoutArgumentsShowsItsSignature() =>
        await AssertHoverAsync("Pressed=", 3, "Pressed", Markdown("signal Pressed()", Signal("BaseButton", "pressed")));

    [Fact]
    public async Task ASignalShowsItsArgumentsByTheirCSharpNames() =>
        await AssertHoverAsync("Toggled=", 3, "Toggled", Markdown("signal Toggled(bool toggledOn)", Signal("BaseButton", "toggled")));

    [Fact]
    public async Task AnEnumValueShowsTheMemberItsValueAndItsDescription() =>
        await AssertHoverAsync("\"Ignore\"", 3, "Ignore", Markdown("Ignore = 2", Constant("Control", "MOUSE_FILTER_IGNORE")));

    [Fact]
    public async Task AFlagsValueShowsTheMemberUnderTheCursor() =>
        await AssertHoverAsync("Fill\"", 2, "Fill", Markdown("Fill = 1", Constant("Control", "SIZE_FILL")));

    [Fact]
    public async Task AKeyReferenceShowsTheEntrysClass() => await AssertHoverAsync("{Panel}", 3, "{Panel}", Markdown("{Panel}: StyleBoxFlat"));

    [Fact]
    public async Task AFormatAttributeLinksTheFormat() => await AssertHoverAsync("x:Groups", 4, "x:Groups", Markdown("x:Groups", FormatLink));

    [Theory]
    [InlineData("  <Button", 1)]
    [InlineData("a note", 2)]
    [InlineData("foo:Bar", 5)]
    [InlineData("foo:Thing", 5)]
    [InlineData("Bogus", 2)]
    [InlineData("\"OnPressed\"", 3)]
    [InlineData("Expand, Fill", 6)]
    public async Task NothingElseHasAHover(string needle, int into)
    {
        await using ServerHarness server = await OpenAsync();

        JsonElement result = await HoverAsync(server, PositionOf(Scene, Scene.IndexOf(needle, StringComparison.Ordinal) + into));

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    [Fact]
    public Task TheFirstCharacterOfAPropertyNameHovers() => AssertEdgeAsync("MouseFilter", 0, "int MouseFilter");

    [Fact]
    public Task TheLastCharacterOfAPropertyNameHovers() => AssertEdgeAsync("MouseFilter", 10, "int MouseFilter");

    [Fact]
    public Task TheEqualsSignAfterANameHasNoHover() => AssertEdgeAsync("MouseFilter", 11, null);

    [Fact]
    public Task AValuesOpeningQuoteHasNoHover() => AssertEdgeAsync("\"Stop\"", 0, null);

    [Fact]
    public Task TheFirstCharacterOfAnEnumValueHovers() => AssertEdgeAsync("Stop", 0, "Stop = 0");

    [Fact]
    public Task AValuesClosingQuoteHasNoHover() => AssertEdgeAsync("\"Stop\"", 5, null);

    [Fact]
    public Task TheFirstCharacterOfAClassTagHovers() => AssertEdgeAsync("<Button", 1, "class Button : BaseButton");

    [Fact]
    public Task TheLastCharacterOfAClassTagHovers() => AssertEdgeAsync("<Button", 6, "class Button : BaseButton");

    [Fact]
    public Task TheSpaceAfterAClassTagHasNoHover() => AssertEdgeAsync("<Button", 7, null);

    [Fact]
    public Task ATagsOpeningBracketHasNoHover() => AssertEdgeAsync("<Button", 0, null);

    [Fact]
    public Task AKeyReferencesOpeningBraceHovers() => AssertEdgeAsync("{Panel}", 0, "{Panel}: StyleBoxFlat");

    [Fact]
    public Task AKeyReferencesClosingQuoteHasNoHover() => AssertEdgeAsync("{Panel}\"", 7, null);

    // A hint that lists classes, some excluded with '-', names the property's type by its first class.
    [Fact]
    public Task AnExcludingClassHintNamesItsFirstClass() => AssertTypedAsync("TextureNormal", "Texture2D TextureNormal");

    [Fact]
    public Task AHintOfTwoClassesNamesTheFirst() => AssertTypedAsync("Material=", "BaseMaterial3D Material");

    // An argument named like a C# keyword is escaped with '@', as GodotSharp names it.
    [Fact]
    public Task AKeywordArgumentIsEscaped() =>
        AssertTypedAsync(
            "InputEvent",
            "signal InputEvent(Node viewport, InputEvent @event, int shapeIdx)",
            Signal("CollisionObject2D", "input_event")
        );

    // A change that lands while the hover runs makes its answer stale: it answers nothing.
    [Fact]
    public async Task AChangeWhileTheHoverRunsGivesNoHover()
    {
        string typed = Scene.Replace("Root", "Typed", StringComparison.Ordinal);
        string changed = Scene.Replace("Root", "Changed", StringComparison.Ordinal);
        using ManualResetEventSlim release = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ServerSeams seams = ServerSeams.Default with
        {
            Parse = (text, kind, metadata) =>
            {
                if (text == typed)
                {
                    started.TrySetResult();
                    release.Wait(TimeSpan.FromMinutes(2));
                }
                return XscnParser.Parse(text, kind, metadata);
            },
        };
        await using ServerHarness server = await OpenAsync(seams);
        try
        {
            await server.ChangeAsync(Uri, 2, typed);
            await server.NextAsync(server.Scheduler.Requested, "the typed change's debounce");

            int key = typed.IndexOf("{Panel}", StringComparison.Ordinal) + 1;
            Task<JsonElement> hovering = HoverAsync(server, PositionOf(typed, key));
            await server.Guard(started.Task, "the hover's parse to start");
            await server.ChangeAsync(Uri, 3, changed);
            await server.NextAsync(server.Scheduler.Requested, "the newer change's debounce");

            release.Set();
            Assert.Equal(JsonValueKind.Null, (await hovering).ValueKind);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    [Fact]
    public async Task ADocumentNotOpenHasNoHover()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();

        JsonElement result = await HoverAsync(server, new LspPosition(0, 1));

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    // A Godot minor with no bundled docs file still gives the signature, with no description.
    [Fact]
    public async Task AVersionWithoutADocsFileGivesTheSignatureAlone()
    {
        ServerSeams seams = ServerSeams.Default with { LoadDocs = _ => ClassDocs.LoadBundled("0.0") };
        await using ServerHarness server = await OpenAsync(seams);
        int offset = Scene.IndexOf("<Button", StringComparison.Ordinal) + 1;

        JsonElement result = await HoverAsync(server, PositionOf(Scene, offset + 2));

        Assert.Equal("markdown", result.GetProperty("contents").GetProperty("kind").GetString());
        Assert.Equal(Markdown("class Button : BaseButton"), result.GetProperty("contents").GetProperty("value").GetString());
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    [Fact]
    public async Task InitializeAdvertisesHover()
    {
        await using ServerHarness server = new();

        JsonElement result = await server.InitializeRawAsync();

        Assert.True(result.GetProperty("capabilities").GetProperty("hoverProvider").GetBoolean());
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    // Hovers inside the name the needle locates (its offset plus `into`) and checks the text and the name's range.
    private static async Task AssertHoverAsync(string needle, int into, string name, string expected)
    {
        int found = Scene.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(found >= 0, $"no {needle} in the scene");
        int start = Scene.IndexOf(name, found, StringComparison.Ordinal);
        await using ServerHarness server = await OpenAsync();

        JsonElement result = await HoverAsync(server, PositionOf(Scene, found + into));

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        JsonElement contents = result.GetProperty("contents");
        Assert.Equal("markdown", contents.GetProperty("kind").GetString());
        Assert.Equal(expected, contents.GetProperty("value").GetString());
        Assert.Equal(new LspRange(PositionOf(Scene, start), PositionOf(Scene, start + name.Length)), RangeOf(result.GetProperty("range")));
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    // Hovers the character `into` the needle in the edges document: null, or a hover whose code block is the signature.
    private static async Task AssertEdgeAsync(string needle, int into, string? signature)
    {
        int found = Edges.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(found >= 0, $"no {needle} in the document");
        await using ServerHarness server = await OpenAsync(text: Edges);

        JsonElement result = await HoverAsync(server, PositionOf(Edges, found + into));

        if (signature is null)
        {
            Assert.Equal(JsonValueKind.Null, result.ValueKind);
        }
        else
        {
            Assert.Equal(JsonValueKind.Object, result.ValueKind);
            Assert.StartsWith(Markdown(signature), result.GetProperty("contents").GetProperty("value").GetString(), StringComparison.Ordinal);
        }
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    // Hovers the second character of the needle in the typed document; the text starts with the signature, then the paragraphs.
    private static async Task AssertTypedAsync(string needle, string signature, params string[] paragraphs)
    {
        await using ServerHarness server = await OpenAsync(text: Typed);

        JsonElement result = await HoverAsync(server, PositionOf(Typed, Typed.IndexOf(needle, StringComparison.Ordinal) + 1));

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        string? value = result.GetProperty("contents").GetProperty("value").GetString();
        if (paragraphs.Length == 0)
        {
            Assert.StartsWith(Markdown(signature), value, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(Markdown(signature, paragraphs), value);
        }
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    private static async Task<ServerHarness> OpenAsync(ServerSeams? seams = null, string text = Scene)
    {
        ServerHarness server = new(seams);
        await server.InitializeAsync();
        await server.OpenAsync(Uri, 1, text);
        await server.NextAsync(server.Published, "the didOpen publish");
        return server;
    }

    // The request as a client sends it.
    private static Task<JsonElement> HoverAsync(ServerHarness server, LspPosition position) =>
        server.RequestObjectAsync(
            "textDocument/hover",
            JsonSerializer.SerializeToElement(
                new { textDocument = new { uri = Uri }, position = new { line = position.Line, character = position.Character } }
            )
        );

    private static string Member(string cls, string name) => Docs.Class(cls)!.Members![name].Description!;

    private static string Signal(string cls, string name) => Docs.Class(cls)!.Signals![name].Description!;

    private static string Constant(string cls, string name) => Docs.Class(cls)!.Constants![name].Description!;

    private static string Markdown(string signature, params string[] paragraphs) =>
        string.Concat(["```csharp\n", signature, "\n```", .. paragraphs.Select(p => "\n\n" + p)]);

    private static LspPosition PositionOf(string text, int offset)
    {
        LinePosition position = new LineMap(text).ZeroBased(offset);
        return new LspPosition(position.Line, position.Column);
    }

    private static LspRange RangeOf(JsonElement range) => new(PointOf(range.GetProperty("start")), PointOf(range.GetProperty("end")));

    private static LspPosition PointOf(JsonElement position) =>
        new(position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
}
