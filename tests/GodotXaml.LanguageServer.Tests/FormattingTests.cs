using System.Text.Json;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer.Tests;

public sealed class FormattingTests
{
    private static readonly string FormatFolder = RepoRoot.PathOf("tests", "fixtures", "xscn", "format");

    private const string Broken = "<Control Name=\"Root\">\n  <Label />\n</Control>";

    public static TheoryData<string> FormatFixtures =>
        new(
            Directory
                .GetFiles(FormatFolder, "*.in.xscn")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Select(name => name[..^".in.xscn".Length])
                .Order(StringComparer.Ordinal)
        );

    [Fact]
    public void TheFormatPairsAreFound() => Assert.True(FormatFixtures.Count >= 3, $"only {FormatFixtures.Count} format pairs");

    // The whole document is replaced by its canonical text, as the .out file holds it.
    [Theory]
    [MemberData(nameof(FormatFixtures))]
    public async Task AFormatPairBecomesOneWholeDocumentEdit(string name)
    {
        string input = File.ReadAllText(Path.Combine(FormatFolder, name + ".in.xscn"));
        string expected = File.ReadAllText(Path.Combine(FormatFolder, name + ".out.xscn"));
        await using ServerHarness server = new();
        await server.InitializeAsync();
        string uri = FileUri(Path.Combine(FormatFolder, name + ".in.xscn"));
        await server.OpenAsync(uri, 1, input);
        await server.NextAsync(server.Published, "the didOpen publish");

        JsonElement edits = await FormatAsync(server, uri);

        JsonElement edit = Assert.Single(edits.EnumerateArray());
        LinePosition end = new LineMap(input).ZeroBased(input.Length);
        Assert.Equal(new LspRange(new LspPosition(0, 0), new LspPosition(end.Line, end.Column)), RangeOf(edit));
        Assert.Equal(expected, edit.GetProperty("newText").GetString());
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    [Fact]
    public async Task ACanonicalFileGivesNoEdits()
    {
        string canonical = File.ReadAllText(Path.Combine(FormatFolder, "defaults.out.xscn"));
        await using ServerHarness server = new();
        await server.InitializeAsync();
        string uri = FileUri(Path.Combine(FormatFolder, "defaults.out.xscn"));
        await server.OpenAsync(uri, 1, canonical);
        await server.NextAsync(server.Published, "the didOpen publish");

        JsonElement edits = await FormatAsync(server, uri);

        Assert.Empty(edits.EnumerateArray());
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    [Fact]
    public async Task ADocumentWithAnErrorGivesNoEditAndAWarning()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();
        string uri = FileUri(Path.Combine(FormatFolder, "Broken.xscn"));
        await server.OpenAsync(uri, 1, Broken);
        await server.NextAsync(server.Published, "the didOpen publish");

        JsonElement edits = await FormatAsync(server, uri);

        Assert.Empty(edits.EnumerateArray());
        ShowMessageParams message = await server.NextAsync(server.Shown, "the formatting warning");
        Assert.Equal(MessageType.Warning, message.Type);
        Assert.Equal("Broken.xscn has errors; fix them before formatting.", message.Message);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Empty(ServerHarness.Remaining(server.Shown));
    }

    // The writer refuses a name holding a character XML 1.0 cannot hold, which no parse produces, so the seam hands
    // the writer the model such a name makes.
    [Fact]
    public async Task AWriterRefusalGivesNoEditAndAnError()
    {
        ServerSeams seams = ServerSeams.Default with
        {
            Parse = (text, kind, metadata) => WithUnwritableRootName(XscnParser.Parse(text, kind, metadata)),
        };
        await using ServerHarness server = new(seams);
        await server.InitializeAsync();
        string uri = FileUri(Path.Combine(FormatFolder, "Unwritable.xscn"));
        await server.OpenAsync(uri, 1, "<Control Name=\"Root\" />");
        await server.NextAsync(server.Published, "the didOpen publish");

        JsonElement edits = await FormatAsync(server, uri);

        Assert.Empty(edits.EnumerateArray());
        ShowMessageParams message = await server.NextAsync(server.Shown, "the formatting error");
        Assert.Equal(MessageType.Error, message.Type);
        Assert.Contains("Unwritable.xscn", message.Message, StringComparison.Ordinal);
        Assert.Contains("U+0001", message.Message, StringComparison.Ordinal);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Empty(ServerHarness.Remaining(server.Shown));
    }

    // A format replaces the whole buffer, so a change that landed while the parse and write ran makes the reply stale:
    // answering with it would wipe what the user typed after asking.
    [Fact]
    public async Task AChangeWhileTheWriteRunsGivesNoEdit()
    {
        const string Original = "<Control Name=\"Root\">\n  <Label Name=\"Original\" />\n</Control>";
        const string Typed = "<Control Name=\"Root\">\n  <Label Name=\"Typed\" />\n</Control>";
        const string Changed = "<Control Name=\"Root\">\n  <Label Name=\"Changed\" />\n</Control>";
        using ManualResetEventSlim release = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ServerSeams seams = ServerSeams.Default with
        {
            Parse = (text, kind, metadata) =>
            {
                if (text == Typed)
                {
                    started.TrySetResult();
                    release.Wait(TimeSpan.FromMinutes(2));
                }
                return XscnParser.Parse(text, kind, metadata);
            },
        };
        await using ServerHarness server = new(seams);
        try
        {
            await server.InitializeAsync();
            string uri = FileUri(Path.Combine(FormatFolder, "Changed.xscn"));
            await server.OpenAsync(uri, 1, Original);
            await server.NextAsync(server.Published, "the didOpen publish");
            await server.ChangeAsync(uri, 2, Typed);
            await server.NextAsync(server.Scheduler.Requested, "the typed change's debounce");

            Task<JsonElement> formatting = FormatAsync(server, uri);
            await server.Guard(started.Task, "the format's parse to start");
            await server.ChangeAsync(uri, 3, Changed);
            await server.NextAsync(server.Scheduler.Requested, "the newer change's debounce");

            release.Set();
            Assert.Empty((await formatting).EnumerateArray());
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    // The range ends at the buffer's own end: its CRLF line breaks and the UTF-16 width of an astral character.
    [Fact]
    public async Task AWholeDocumentRangeCountsCarriageReturnsAndSurrogatePairs()
    {
        const string Text = "<Control Name=\"Root\">\r\n  <Label Name=\"A\" Text=\"\U0001F600\" /></Control>";
        await using ServerHarness server = new();
        await server.InitializeAsync();
        string uri = FileUri(Path.Combine(FormatFolder, "Astral.xscn"));
        await server.OpenAsync(uri, 1, Text);
        await server.NextAsync(server.Published, "the didOpen publish");

        JsonElement edits = await FormatAsync(server, uri);

        JsonElement edit = Assert.Single(edits.EnumerateArray());
        Assert.Equal(new LspRange(new LspPosition(0, 0), new LspPosition(1, 40)), RangeOf(edit));
        Assert.Contains("\U0001F600", edit.GetProperty("newText").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    [Fact]
    public async Task AnUnknownDocumentGivesNoEdits()
    {
        await using ServerHarness server = new();
        await server.InitializeAsync();

        JsonElement edits = await FormatAsync(server, FileUri(Path.Combine(FormatFolder, "Never.xscn")));

        Assert.Empty(edits.EnumerateArray());
        Assert.False(server.Shown.TryRead(out _), "a message was shown for a document not open");
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Empty(ServerHarness.Remaining(server.Shown));
    }

    [Fact]
    public async Task InitializeAdvertisesDocumentFormatting()
    {
        await using ServerHarness server = new();

        JsonElement result = await server.InitializeRawAsync();

        Assert.True(result.GetProperty("capabilities").GetProperty("documentFormattingProvider").GetBoolean());
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
    }

    [Fact]
    public async Task FormattingRefusesAFileWhoseMarkerIsNotCarried()
    {
        ServerSeams seams = ServerSeams.Default with { LoadMetadata = MetadataSeams.Loader };
        await using ServerHarness server = new(seams);
        await server.InitializeAsync();

        string refused = FileUri(Path.Combine(FormatFolder, "Marker49.xscn"));
        await server.OpenAsync(refused, 1, Marked("4.9"));
        await server.NextAsync(server.Published, "the refused file's publish");
        await server.NextAsync(server.Shown, "the marker's notice");

        JsonElement refusedEdits = await FormatAsync(server, refused);

        Assert.Empty(refusedEdits.EnumerateArray());
        ShowMessageParams refusal = await server.NextAsync(server.Shown, "the formatting refusal");
        Assert.Equal(MessageType.Warning, refusal.Type);
        Assert.Equal($"Marker49.xscn: {XscnMarker.NotWritten("4.9", "4.8")}", refusal.Message);

        // A marker the server carries is written with, so the file keeps its own version and marker.
        string kept = FileUri(Path.Combine(FormatFolder, "Marker48.xscn"));
        await server.OpenAsync(kept, 1, Marked("4.8"));
        await server.NextAsync(server.Published, "the kept file's publish");

        JsonElement keptEdits = await FormatAsync(server, kept);

        JsonElement edit = Assert.Single(keptEdits.EnumerateArray());
        Assert.Contains("x:Godot=\"4.8\"", edit.GetProperty("newText").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Empty(ServerHarness.Remaining(server.Shown));
    }

    // The request as a client sends it: the document's URI and formatting options the server ignores.
    private static Task<JsonElement> FormatAsync(ServerHarness server, string uri) =>
        server.RequestObjectAsync(
            "textDocument/formatting",
            JsonSerializer.SerializeToElement(new { textDocument = new { uri }, options = new { tabSize = 2, insertSpaces = true } })
        );

    private static string FileUri(string path) => new Uri(path).AbsoluteUri;

    // A scene marked with a Godot version, written so that its canonical form is a whole-document edit.
    private static string Marked(string marker) =>
        $"<Control xmlns:x=\"{XscnParser.FormatNamespace}\" x:Godot=\"{marker}\" Name=\"Root\"><Label Name=\"A\" /></Control>";

    private static XscnParseResult WithUnwritableRootName(XscnParseResult result)
    {
        var root = (XscnNode)result.Document.Root;
        return result with { Document = result.Document with { Root = root with { Name = "A\u0001B" } } };
    }

    private static LspRange RangeOf(JsonElement edit)
    {
        JsonElement range = edit.GetProperty("range");
        return new LspRange(PositionOf(range.GetProperty("start")), PositionOf(range.GetProperty("end")));
    }

    private static LspPosition PositionOf(JsonElement position) =>
        new(position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
}
