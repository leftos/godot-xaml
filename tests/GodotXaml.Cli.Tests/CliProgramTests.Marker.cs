using System.Text;
using System.Text.Json.Nodes;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Cli.Tests;

/// <summary>The metadata a run reads and writes each file with, chosen by the file's <c>x:Godot</c> marker.</summary>
public sealed partial class CliProgramTests
{
    // A Label whose Text attribute only the newest carried version types: the older one's metadata is the bundled file
    // without Label's text property, so Text is a script property with no type and its value is not Godot value text.
    private const string TextScene = "<Label Name=\"Root\" Text=\"a\" />";
    private const string Older = "4.6";
    private const string NewestCarried = "4.7";
    private const string Unknown = "4.9";

    private static readonly Lazy<JsonObject> OlderMetadata = new(OlderMetadataJson);

    [Fact]
    public void CheckReadsAMarkedFileWithItsMarkersMetadata()
    {
        MetadataVersions versions = CarryingAnOlderVersion();
        string marked = Write("older.xscn", WithMarker(TextScene, Older));
        string unmarked = Write("newest.xscn", TextScene);

        RunResult run = RunWith(versions, "check", marked, unmarked);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(2, run.Lines.Length);
        Assert.StartsWith($"{marked}(1,", run.Lines[0], StringComparison.Ordinal);
        Assert.Contains("error GX0105", run.Lines[0], StringComparison.Ordinal);
        Assert.Equal("2 file(s) checked, 1 error(s), 0 warning(s)", run.Lines[1]);
    }

    [Fact]
    public void FormatKeepsACarriedMarker()
    {
        MetadataVersions versions = CarryingAnOlderVersion();
        string file = Write("older.xscn", WithMarker("<Control  Name=\"Root\" />", Older));

        RunResult run = RunWith(versions, "format", file);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"formatted {file}", "1 of 1 file(s) changed"], run.Lines);
        string written = File.ReadAllText(file);
        Assert.Contains($"x:Godot=\"{Older}\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain($"x:Godot=\"{NewestCarried}\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUncarriedMarkerWarnsOncePerRun()
    {
        MetadataVersions versions = CarryingAnOlderVersion();
        string first = Write("a.xscn", WithMarker(TextScene, Unknown));
        Write("b.xscn", WithMarker(TextScene, Unknown));

        RunResult run = RunWith(versions, "check", folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{first}: warning GXT0004: written with Godot {Unknown}'s metadata, which this tool does not carry; "
                    + $"reading it with {NewestCarried}'s",
                "2 file(s) checked, 0 error(s), 1 warning(s)",
            ],
            run.Lines
        );
    }

    [Fact]
    public void FormatRefusesAFileWhoseMarkerIsNotCarried()
    {
        MetadataVersions versions = CarryingAnOlderVersion();
        string file = Write("unknown.xscn", WithMarker("<Control  Name=\"Root\" />", Unknown));
        byte[] before = File.ReadAllBytes(file);

        RunResult run = RunWith(versions, "format", file);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(
            [
                $"{file}: warning GXT0004: written with Godot {Unknown}'s metadata, which this tool does not carry; "
                    + $"reading it with {NewestCarried}'s",
                $"{file}: error GXT0005: written with Godot {Unknown}'s metadata, which this tool does not carry (it has "
                    + $"{NewestCarried}'s); not written, so the file keeps {Unknown}'s typing and marker",
                "0 of 1 file(s) changed",
            ],
            run.Lines
        );
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public void AnExplicitMetadataFileReadsEveryFile()
    {
        string metadata = Write("older.json", OlderMetadata.Value.ToJsonString());
        string file = Write("scene.xscn", WithMarker(TextScene, Unknown));

        RunResult run = Run("check", file, "--metadata", metadata);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(2, run.Lines.Length);
        Assert.StartsWith($"{file}(1,", run.Lines[0], StringComparison.Ordinal);
        Assert.Contains("error GX0105", run.Lines[0], StringComparison.Ordinal);
        Assert.Equal("1 file(s) checked, 1 error(s), 0 warning(s)", run.Lines[1]);
    }

    [Fact]
    public void ConvertBackReadsAMarkedFileWithItsMarkersMetadata()
    {
        MetadataVersions versions = CarryingAnOlderVersion();
        string file = Write("scene.xscn", WithMarker(TextScene, Older));

        RunResult run = RunWith(versions, "convert", file);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(2, run.Lines.Length);
        Assert.StartsWith($"{file}(1,", run.Lines[0], StringComparison.Ordinal);
        Assert.Contains("error GX0105", run.Lines[0], StringComparison.Ordinal);
        Assert.Equal("0 converted, 0 refused, 1 error(s), 0 warning(s)", run.Lines[1]);
        Assert.False(File.Exists(Path.ChangeExtension(file, ".tscn")));
    }

    [Fact]
    public void ConvertBackCountsAnUncarriedMarkerAsOneWarning()
    {
        MetadataVersions versions = CarryingAnOlderVersion();
        Write("project.godot", "config_version=5\n");
        string first = Write("a.xscn", WithMarker("<Control Name=\"Root\" />", Unknown));
        Write("b.xscn", WithMarker("<Control Name=\"Root\" />", Unknown));

        RunResult run = RunWith(versions, "convert", "--project", folder, "--back");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{first}: warning GXT0004: written with Godot {Unknown}'s metadata, which this tool does not carry; "
                    + $"reading it with {NewestCarried}'s",
            ],
            [.. run.Lines.Where(line => line.Contains("GXT0004", StringComparison.Ordinal))]
        );
        Assert.Equal("2 converted, 0 refused, 0 error(s), 1 warning(s)", run.Lines[^1]);
    }

    // The two attributes a root carries before its own: the format namespace, and the marker this test sets.
    private static string WithMarker(string scene, string marker)
    {
        int tag = scene.IndexOf(' ', StringComparison.Ordinal) + 1;
        return scene[..tag] + $"xmlns:x=\"{XscnParser.FormatNamespace}\" x:Godot=\"{marker}\" " + scene[tag..];
    }

    // The versions a run carries for one test: the bundled 4.7, and 4.6, which is the bundled JSON with its version set to
    // 4.6 and Label's text property removed. 4.9 is carried by neither, so a file marked 4.9 is read with 4.7.
    private static MetadataVersions CarryingAnOlderVersion()
    {
        var older = GodotMetadata.Load(new MemoryStream(Encoding.UTF8.GetBytes(OlderMetadata.Value.ToJsonString())));
        return new MetadataVersions(
            [Older, NewestCarried],
            version =>
                version switch
                {
                    Older => older,
                    NewestCarried => GodotMetadata.LoadBundled(NewestCarried),
                    _ => throw new ArgumentException($"This test carries no metadata for Godot {version}.", nameof(version)),
                }
        );
    }

    private static JsonObject OlderMetadataJson()
    {
        JsonObject json = JsonNode.Parse(File.ReadAllText(RepoRoot.PathOf("metadata", "godot-4.7.json")))!.AsObject();
        json["version"] = Older;
        JsonArray properties = json["classes"]!.AsObject()["Label"]!.AsObject()["properties"]!.AsArray();
        properties.Remove(properties.First(property => (string?)property!["name"] == "text"));
        return json;
    }

    // Runs the tool with the versions the test's run carries.
    private static RunResult RunWith(MetadataVersions versions, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = CliProgram.Run(
            args,
            output,
            error,
            new CliSeams(folder => Directory.EnumerateFiles(folder), Environment.CurrentDirectory, versions)
        );
        return new RunResult(exitCode, output.ToString(), error.ToString());
    }
}
