using GodotXaml.Core.Projects;

namespace GodotXaml.Core.Tests.Projects;

public sealed class GodotProjectTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("gxaml-project-").FullName;

    public GodotProjectTests()
    {
        File.WriteAllText(Path.Combine(root, "project.godot"), "");
        Directory.CreateDirectory(Path.Combine(root, "UI"));
        File.WriteAllText(Path.Combine(root, "UI", "Button.tscn"), "");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void DiskPathRestoresTheCasingOfExistingSegments() =>
        Assert.Equal(Path.Combine(root, "UI", "Button.tscn"), GodotProject.DiskPath(root, "res://ui/button.TSCN"));

    [Fact]
    public void DiskPathKeepsAMissingTailAsWritten() =>
        Assert.Equal(Path.Combine(root, "UI", "new", "Thing.tscn"), GodotProject.DiskPath(root, "res://ui/new/Thing.tscn"));

    [Fact]
    public void DiskPathOfTheProjectItselfIsTheRoot() => Assert.Equal(root, GodotProject.DiskPath(root, "res://"));

    [Fact]
    public void DiskPathRejectsAPathOutsideRes() => Assert.Throws<ArgumentException>(() => GodotProject.DiskPath(root, "user://save.tres"));

    [Fact]
    public void DiskPathIsTheInverseOfResPath()
    {
        const string resPath = "res://UI/Button.tscn";

        Assert.Equal(resPath, GodotProject.ResPath(root, GodotProject.DiskPath(root, resPath)));
    }
}
