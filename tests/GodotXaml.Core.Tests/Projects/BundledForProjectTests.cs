using GodotXaml.Core.Metadata;

namespace GodotXaml.Core.Tests.Projects;

public sealed class BundledForProjectTests
{
    private static string Newest => GodotMetadata.BundledVersions.MaxBy(v => Version.Parse(v))!;

    [Fact]
    public void LoadsTheProjectsBundledVersion()
    {
        var metadata = GodotMetadata.LoadBundledForProject("4.7", out string version, out string? warning);

        Assert.Equal("4.7", version);
        Assert.Null(warning);
        Assert.NotNull(metadata.FindClass("Button"));
    }

    [Fact]
    public void FallsBackToTheNewestForAVersionNotBundled()
    {
        GodotMetadata.LoadBundledForProject("4.9", out string version, out string? warning);

        Assert.Equal(Newest, version);
        Assert.Equal($"Godot 4.9 has no bundled metadata; using {Newest}", warning);
    }

    [Fact]
    public void FallsBackToTheNewestWhenTheProjectNamesNoVersion()
    {
        GodotMetadata.LoadBundledForProject(null, out string version, out string? warning);

        Assert.Equal(Newest, version);
        Assert.Equal($"the project names no Godot version; using {Newest}", warning);
    }
}
