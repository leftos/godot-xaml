namespace GodotXaml.Cli.Tests;

/// <summary>The <c>convert</c> command, over the global class a scripted resource names.</summary>
public sealed partial class CliProgramTests
{
    private const string ArrowLookResource =
        "<Resource xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\" x:Godot=\"4.7\" "
        + "Script=\"res://Scripts/Components/ArrowLook.cs\" />";

    // The project cache Godot 4.7.2 writes: a ConfigFile whose only section is empty, so no header, holding one array.
    private const string ArrowLookCache =
        """
            list=[{
            "base": &"Resource",
            "class": &"ArrowLook",
            "icon": "",
            "is_abstract": false,
            "is_tool": false,
            "language": &"C#",
            "path": "res://Scripts/Components/ArrowLook.cs"
            }]
            """ + "\n";

    private const string OtherCache =
        """
            list=[{
            "base": &"Resource",
            "class": &"Other",
            "icon": "",
            "is_abstract": false,
            "is_tool": false,
            "language": &"C#",
            "path": "res://Scripts/Other.cs"
            }]
            """ + "\n";

    private const string MalformedCache = "list=[{";

    [Fact]
    public void BackKeepsAGlobalClassScriptClass()
    {
        string xres = ScriptedProject();
        Write(".godot/global_script_class_cache.cfg", ArrowLookCache);

        RunResult run = RunIn(folder, "convert", xres);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("script_class=\"ArrowLook\"", FirstLine(Path.ChangeExtension(xres, ".tres")), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Lines, line => line.Contains("GXC0106", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectBackKeepsAGlobalClassScriptClass()
    {
        string xres = ScriptedProject();
        Write(".godot/global_script_class_cache.cfg", ArrowLookCache);

        RunResult run = RunProject("--back");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("script_class=\"ArrowLook\"", FirstLine(Path.ChangeExtension(xres, ".tres")), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Lines, line => line.Contains("GXC0106", StringComparison.Ordinal));
    }

    [Fact]
    public void BackWithoutAClassCacheWarnsGxc0106()
    {
        string xres = ScriptedProject();

        RunResult run = RunIn(folder, "convert", xres);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("script_class=", FirstLine(Path.ChangeExtension(xres, ".tres")), StringComparison.Ordinal);
        Assert.Single(run.Lines, line => line.Contains("warning GXC0106", StringComparison.Ordinal));
    }

    [Fact]
    public void BackWithAMalformedClassCacheWarnsGxc0106()
    {
        string xres = ScriptedProject();
        Write(".godot/global_script_class_cache.cfg", MalformedCache);

        RunResult run = RunIn(folder, "convert", xres);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("script_class=", FirstLine(Path.ChangeExtension(xres, ".tres")), StringComparison.Ordinal);
        Assert.Single(run.Lines, line => line.Contains("warning GXC0106", StringComparison.Ordinal));
    }

    [Fact]
    public void BackWithACacheNotStartingWithListWarnsGxc0106()
    {
        string xres = ScriptedProject();
        Write(".godot/global_script_class_cache.cfg", "other=[]\n");

        RunResult run = RunIn(folder, "convert", xres);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("script_class=", FirstLine(Path.ChangeExtension(xres, ".tres")), StringComparison.Ordinal);
        Assert.Single(run.Lines, line => line.Contains("warning GXC0106", StringComparison.Ordinal));
    }

    [Fact]
    public void BackOutsideAProjectWarnsGxc0106()
    {
        Write("godot-xaml.json", "{}");
        Write("Scripts/Components/ArrowLook.cs", "public partial class ArrowLook : Godot.Resource {}\n");
        string xres = Write("Art/arrow.xres", ArrowLookResource);
        Write(".godot/global_script_class_cache.cfg", ArrowLookCache);

        RunResult run = RunIn(folder, "convert", xres);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("script_class=", FirstLine(Path.ChangeExtension(xres, ".tres")), StringComparison.Ordinal);
        Assert.Single(run.Lines, line => line.Contains("warning GXC0106", StringComparison.Ordinal));
    }

    [Fact]
    public void BackOfAScriptTheCacheDoesNotListWritesNoScriptClass()
    {
        string xres = ScriptedProject();
        Write(".godot/global_script_class_cache.cfg", OtherCache);

        RunResult run = RunIn(folder, "convert", xres);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("script_class=", FirstLine(Path.ChangeExtension(xres, ".tres")), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Lines, line => line.Contains("GXC0106", StringComparison.Ordinal));
    }

    // A project holding one scripted resource, the script it names and the config that keeps a config above the temp
    // folder out of the run; the class cache is written by the test that needs one.
    private string ScriptedProject()
    {
        Write("godot-xaml.json", "{}");
        Write("project.godot", "config_version=5\n");
        Write("Scripts/Components/ArrowLook.cs", "public partial class ArrowLook : Godot.Resource {}\n");
        return Write("Art/arrow.xres", ArrowLookResource);
    }

    private static string FirstLine(string path) => File.ReadLines(path).First();
}
