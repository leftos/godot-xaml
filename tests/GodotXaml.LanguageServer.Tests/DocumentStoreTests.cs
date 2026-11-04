using GodotXaml.Core.Conversion;

namespace GodotXaml.LanguageServer.Tests;

public sealed class DocumentStoreTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("gxaml-ls-store-").FullName;

    private readonly DocumentStore store = new();

    public DocumentStoreTests()
    {
        File.WriteAllText(Path.Combine(root, "project.godot"), "config_version=5\n");
        File.WriteAllText(Referrer, "<Control Name=\"Main\" />");
        File.WriteAllText(Referenced, "<Control Name=\"OnDisk\" />");
    }

    private string Referrer => Path.Combine(root, "Main.xscn");

    private string Referenced => Path.Combine(root, "Part.xscn");

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void AnOpenSceneIsReadFromItsBufferNotFromDisk()
    {
        store.Open(new Uri(Referenced).AbsoluteUri, 3, "<Control Name=\"InBuffer\" />");

        SceneFile? file = ((ISceneFiles)store).Read(Referrer, "res://Part.xscn", uid: null);

        Assert.NotNull(file);
        Assert.Equal(Referenced, file.Key, DocumentStore.PathComparer);
        Assert.Equal("<Control Name=\"InBuffer\" />", file.Text);
    }

    [Fact]
    public void AClosedSceneIsReadFromDisk()
    {
        string uri = new Uri(Referenced).AbsoluteUri;
        store.Open(uri, 3, "<Control Name=\"InBuffer\" />");
        store.Close(uri);

        SceneFile? file = ((ISceneFiles)store).Read(Referrer, "res://Part.xscn", uid: null);

        Assert.NotNull(file);
        Assert.Equal("<Control Name=\"OnDisk\" />", file.Text);
    }

    [Fact]
    public void ARelativeReferenceIsReadBesideTheReferrer()
    {
        SceneFile? file = ((ISceneFiles)store).Read(Referrer, "Part.xscn", uid: null);

        Assert.NotNull(file);
        Assert.Equal("<Control Name=\"OnDisk\" />", file.Text);
    }

    [Fact]
    public void AVsCodeFileUriIsKeyedByItsPathAndOverlaysTheDisk()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "VS Code's encoded drive colon is a Windows form");
        string spaced = Path.Combine(root, "My Proj", "Scene.xscn");

        Assert.True(DocumentStore.TryDescribe(VsCode.FileUri(spaced), out string? path, out _));
        Assert.Equal(spaced, path, DocumentStore.PathComparer);

        store.Open(VsCode.FileUri(Referenced), 3, "<Control Name=\"InBuffer\" />");
        SceneFile? file = ((ISceneFiles)store).Read(Referrer, "res://Part.xscn", uid: null);
        Assert.NotNull(file);
        Assert.Equal("<Control Name=\"InBuffer\" />", file.Text);
    }

    [Fact]
    public void AMissingSceneGivesNull() => Assert.Null(((ISceneFiles)store).Read(Referrer, "res://Missing.xscn", uid: null));

    [Fact]
    public void AnOpenBufferIsFoundByItsUriBesideTheOneAChangeMakesAndNoneOnceClosed()
    {
        string uri = new Uri(Referenced).AbsoluteUri;
        Assert.Null(store.Find(uri));

        store.Open(uri, 3, "<Control Name=\"InBuffer\" />");
        OpenDocument open = Assert.IsType<OpenDocument>(store.Find(uri));
        Assert.Equal(3, open.Version);
        Assert.Equal("<Control Name=\"InBuffer\" />", open.Text);

        store.Change(uri, 4, "<Control Name=\"Changed\" />");
        OpenDocument changed = Assert.IsType<OpenDocument>(store.Find(uri));
        Assert.Equal(4, changed.Version);
        Assert.Equal("<Control Name=\"Changed\" />", changed.Text);

        store.Close(uri);
        Assert.Null(store.Find(uri));
    }
}
