using System.Collections.Concurrent;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer.Tests;

public sealed class ProjectTests : IDisposable
{
    // A Label without a Name: GX0104 under any metadata.
    private const string Broken = "<Control Name=\"Root\">\n  <Label />\n</Control>";

    private readonly string root = Directory.CreateTempSubdirectory("gxaml-ls-project-").FullName;

    private readonly ConcurrentQueue<string?> asked = new();

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task AProjectNamingABundledVersionIsReadWithItAndTheVersionIsLogged()
    {
        WriteProject("4.7");
        await using ServerHarness server = new(Recording(ServerSeams.Default.LoadMetadata));
        await server.InitializeAsync();

        await server.OpenAsync(SceneUri("Main.xscn"), 1, Broken);
        PublishDiagnosticsParams published = await server.NextAsync(server.Published, "the publish");

        Assert.Single(published.Diagnostics);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Equal(["4.7"], asked);
        Assert.Contains(
            ServerHarness.Remaining(server.Logged),
            log => log.Type == MessageType.Info && log.Message.Contains("Godot 4.7", StringComparison.Ordinal)
        );
        Assert.Empty(ServerHarness.Remaining(server.Shown));
    }

    [Fact]
    public async Task AProjectNamingAnUnbundledVersionWarnsOnceAndUsesTheNewestBundled()
    {
        WriteProject("4.2");
        await using ServerHarness server = new(Recording(ServerSeams.Default.LoadMetadata));
        await server.InitializeAsync();

        await server.OpenAsync(SceneUri("One.xscn"), 1, Broken);
        await server.NextAsync(server.Published, "the first document's publish");
        await server.OpenAsync(SceneUri("Two.xscn"), 1, Broken);
        await server.NextAsync(server.Published, "the second document's publish");

        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        ShowMessageParams shown = Assert.Single(ServerHarness.Remaining(server.Shown));
        Assert.Equal(MessageType.Warning, shown.Type);
        Assert.Contains("4.2", shown.Message, StringComparison.Ordinal);
        Assert.Equal(["4.2"], asked);
        Assert.Contains(
            ServerHarness.Remaining(server.Logged),
            log => log.Type == MessageType.Info && log.Message.Contains("Godot 4.7", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task AMetadataLoadFailureIsShownAsAnErrorAndPublishesNothing()
    {
        WriteProject("4.7");
        ServerSeams seams = ServerSeams.Default with { LoadMetadata = _ => throw new InvalidOperationException("the metadata is unreadable") };
        await using ServerHarness server = new(seams);
        await server.InitializeAsync();

        await server.OpenAsync(SceneUri("Main.xscn"), 1, Broken);
        ShowMessageParams shown = await server.NextAsync(server.Shown, "the failure's message");

        Assert.Equal(MessageType.Error, shown.Type);
        Assert.Contains("the metadata is unreadable", shown.Message, StringComparison.Ordinal);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Empty(ServerHarness.Remaining(server.Published));
    }

    [Fact]
    public async Task AVsCodeFileUriFindsItsProject()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "VS Code's encoded drive colon is a Windows form");
        string folder = Directory.CreateDirectory(Path.Combine(root, "My Proj")).FullName;
        File.WriteAllText(Path.Combine(folder, "project.godot"), "[application]\nconfig/features=PackedStringArray(\"4.7\")\n");
        await using ServerHarness server = new(Recording(ServerSeams.Default.LoadMetadata));
        await server.InitializeAsync();

        string uri = VsCode.FileUri(Path.Combine(folder, "Scene.xscn"));
        await server.OpenAsync(uri, 1, Broken);
        PublishDiagnosticsParams published = await server.NextAsync(server.Published, "the publish");

        Assert.Equal(uri, published.Uri);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Equal(["4.7"], asked);
    }

    [Fact]
    public async Task AnUnreadableProjectGodotIsShownAsUnreadableOnce()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to read a file another handle holds unshared");
        WriteProject("4.7");
        await using ServerHarness server = new();
        await server.InitializeAsync();

        using (new FileStream(Path.Combine(root, "project.godot"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await server.OpenAsync(SceneUri("One.xscn"), 1, Broken);
            await server.NextAsync(server.Published, "the first document's publish");
            await server.OpenAsync(SceneUri("Two.xscn"), 1, Broken);
            await server.NextAsync(server.Published, "the second document's publish");
        }

        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        ShowMessageParams shown = Assert.Single(ServerHarness.Remaining(server.Shown));
        Assert.Equal(MessageType.Warning, shown.Type);
        Assert.Contains("project.godot could not be read", shown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("names no Godot version", shown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMarkedFileIsReadWithItsMarkersMetadata()
    {
        WriteProject("4.7");
        await using ServerHarness server = new(Recording(MetadataSeams.Loader));
        await server.InitializeAsync();

        await server.OpenAsync(SceneUri("Marked.xscn"), 1, Scene("4.8"));
        PublishDiagnosticsParams marked = await server.NextAsync(server.Published, "the marked file's publish");
        await server.OpenAsync(SceneUri("Unmarked.xscn"), 1, Scene(null));
        PublishDiagnosticsParams plain = await server.NextAsync(server.Published, "the unmarked file's publish");

        // 4.8's metadata has no Label.text, so Text is an unknown property whose value does not read as Godot value text.
        LspDiagnostic unknown = Assert.Single(marked.Diagnostics);
        Assert.Equal(XscnCodes.InvalidValue, unknown.Code);
        Assert.Contains("Unexpected identifier 'a'", unknown.Message, StringComparison.Ordinal);

        // The same file with no marker keeps the project's 4.7, where Text is a String property.
        Assert.Empty(plain.Diagnostics);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());
        Assert.Equal(["4.7", "4.8"], asked);
    }

    [Fact]
    public async Task AnUnmarkedFileKeepsTheProjectsVersion()
    {
        WriteProject("4.7");
        await using ServerHarness server = new(Recording(MetadataSeams.Loader));
        await server.InitializeAsync();

        await server.OpenAsync(SceneUri("Plain.xscn"), 1, Scene(null));
        PublishDiagnosticsParams published = await server.NextAsync(server.Published, "the publish");

        Assert.Empty(published.Diagnostics);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());

        // The loader carries 4.8 too, and it is the newest; the file is still read with the project's 4.7.
        Assert.Equal(["4.7"], asked);
    }

    [Fact]
    public async Task AMarkerTheServerDoesNotCarryWarnsOnce()
    {
        WriteProject("4.7");
        await using ServerHarness server = new(Recording(MetadataSeams.Loader));
        await server.InitializeAsync();

        await server.OpenAsync(SceneUri("One.xscn"), 1, Scene("4.9"));
        PublishDiagnosticsParams first = await server.NextAsync(server.Published, "the first file's publish");
        await server.OpenAsync(SceneUri("Two.xscn"), 1, Scene("4.9"));
        PublishDiagnosticsParams second = await server.NextAsync(server.Published, "the second file's publish");

        // Both read with 4.8, the newest the server carries: Text is unknown to it.
        Assert.Equal(XscnCodes.InvalidValue, Assert.Single(first.Diagnostics).Code);
        Assert.Equal(XscnCodes.InvalidValue, Assert.Single(second.Diagnostics).Code);
        Assert.Equal(0, await server.ShutdownAndDrainAsync());

        ShowMessageParams shown = Assert.Single(ServerHarness.Remaining(server.Shown));
        Assert.Equal(MessageType.Warning, shown.Type);
        Assert.Equal($"One.xscn: {XscnMarker.NotCarried("4.9", "4.8")}", shown.Message);
    }

    // A scene whose Label's Text is a string property in 4.7's metadata and, in 4.8's, an unknown property whose unquoted
    // value is not Godot value text: which metadata read the file shows in its diagnostics.
    private static string Scene(string? marker) =>
        $"<Control xmlns:x=\"{XscnParser.FormatNamespace}\"{(marker is null ? "" : $" x:Godot=\"{marker}\"")} Name=\"Root\">"
        + "<Label Name=\"Text\" Text=\"a\" /></Control>";

    private ServerSeams Recording(MetadataLoader inner) =>
        ServerSeams.Default with
        {
            LoadMetadata = version =>
            {
                asked.Enqueue(version);
                return inner(version);
            },
        };

    private void WriteProject(string version) =>
        File.WriteAllText(
            Path.Combine(root, "project.godot"),
            $"config_version=5\n\n[application]\n\nconfig/name=\"Test\"\nconfig/features=PackedStringArray(\"{version}\", \"C#\", \"Forward Plus\")\n"
        );

    private string SceneUri(string name)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "scenes")).FullName;
        return new Uri(Path.Combine(folder, name)).AbsoluteUri;
    }
}
