using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Metadata;

public sealed class BundledMetadataTests
{
    [Fact]
    public void LoadsTheBundledMetadata()
    {
        var metadata = GodotMetadata.LoadBundled("4.7");
        GodotClassInfo button = metadata.FindClass("Button")!;
        GodotPropertyDefault focusMode = metadata.FindDefault(button, "focus_mode")!;

        Assert.Equal("2", focusMode.Text);
        Assert.Equal(new GodotInt(2), focusMode.Value);
    }

    [Fact]
    public void ListsTheVersionsTheAssemblyCarries() => Assert.Equal(["4.7", "4.8"], GodotMetadata.BundledVersions);

    [Theory]
    [InlineData("4.7")]
    [InlineData("4.8")]
    public void TheBundledMetadataNamesItsVersion(string version) => Assert.Equal(version, GodotMetadata.LoadBundled(version).Version);

    [Fact]
    public void RejectsAVersionTheAssemblyDoesNotCarry()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => GodotMetadata.LoadBundled("4.6"));

        Assert.Contains("4.6", error.Message, StringComparison.Ordinal);
        Assert.Contains("4.7", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BundledMetadataIsTheRepositoryFile()
    {
        var bundled = GodotMetadata.LoadBundled("4.7");
        var file = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

        Assert.Equal(file.Classes.Count(), bundled.Classes.Count());
        Assert.Equal(
            file.FindDefault(file.FindClass("Label")!, "mouse_filter")!.Value,
            bundled.FindDefault(bundled.FindClass("Label")!, "mouse_filter")!.Value
        );
    }
}
