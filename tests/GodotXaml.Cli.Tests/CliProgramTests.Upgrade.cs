using System.Text;
using System.Text.Json.Nodes;
using GodotXaml.Core.Metadata;

namespace GodotXaml.Cli.Tests;

/// <summary><c>upgrade</c>: a project's files read with one Godot version's metadata and written with another's.</summary>
public sealed partial class CliProgramTests
{
    // The hand-made pair: 4.6 is the bundled 4.7 JSON with Control.MouseFilter's Ignore named Skip at the same value, and
    // a Label property, legacy_text, that 4.7 does not have.
    private const string UpgradeFrom = "4.6";
    private const string UpgradeTo = "4.7";
    private const string SkipScene = "<Control Name=\"Root\" MouseFilter=\"Skip\" />";
    private const string IgnoreScene = "<Control Name=\"Root\" MouseFilter=\"Ignore\" />";

    private static readonly Lazy<JsonObject> UpgradeFromMetadata = new(UpgradeFromMetadataJson);

    [Fact]
    public void UpgradeRewritesEachFileWithTheNewMetadata()
    {
        string file = WriteUpgradeProject("ui/main.xscn", WithMarker(SkipScene, UpgradeFrom));

        RunResult run = RunUpgrade("--to", UpgradeTo, folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [$"1 file(s) backed up to {Assert.Single(BackupFolders())}", $"{file}: upgraded", "1 of 1 file(s) upgraded, 0 warning(s)"],
            run.Lines
        );
        string written = File.ReadAllText(file);
        Assert.Contains($"x:Godot=\"{UpgradeTo}\"", written, StringComparison.Ordinal);
        Assert.Contains("MouseFilter=\"Ignore\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Skip", written, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnmarkedFileReadsWithFrom()
    {
        string file = WriteUpgradeProject("main.xscn", SkipScene);

        RunResult run = RunUpgrade("--from", UpgradeFrom, "--to", UpgradeTo, folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"{file}: upgraded", "1 of 1 file(s) upgraded, 0 warning(s)"], run.Lines[1..]);
        string written = File.ReadAllText(file);
        Assert.Contains($"x:Godot=\"{UpgradeTo}\"", written, StringComparison.Ordinal);
        Assert.Contains("MouseFilter=\"Ignore\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnmarkedFileWithoutFromIsGXT0007()
    {
        string file = WriteUpgradeProject("main.xscn", SkipScene);
        byte[] before = File.ReadAllBytes(file);

        RunResult run = RunUpgrade("--to", UpgradeTo, folder);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(
            [$"{file}: error GXT0007: no x:Godot marker; pass --from with the version it was written with", "0 of 1 file(s) upgraded, 0 warning(s)"],
            run.Lines
        );
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public void AMarkerThatDisagreesWithFromIsGXT0006()
    {
        string file = WriteUpgradeProject("main.xscn", WithMarker(SkipScene, UpgradeFrom));
        string[] folders = AllFolders();
        Dictionary<string, byte[]> before = Snapshot(AllFiles());

        RunResult run = RunUpgrade("--from", UpgradeTo, "--to", UpgradeTo, folder);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(
            [$"{file}: error GXT0006: x:Godot is {UpgradeFrom} but --from is {UpgradeTo}", "0 of 1 file(s) upgraded, 0 warning(s)"],
            run.Lines
        );
        Assert.Equal(folders, AllFolders());
        Assert.Equal(before.Keys, AllFiles());
        AssertUnchanged(before);
    }

    [Fact]
    public void ARemovedPropertyIsKeptWithItsValueTextAndAWarning()
    {
        string file = WriteUpgradeProject("main.xscn", WithMarker("<Label Name=\"Root\" LegacyText=\"old\" />", UpgradeFrom));

        RunResult run = RunUpgrade("--to", UpgradeTo, folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{file}: warning GXT0008: Label.legacy_text is not in Godot {UpgradeTo}'s metadata; kept as a script property with its value text",
                $"{file}: upgraded",
                "1 of 1 file(s) upgraded, 1 warning(s)",
            ],
            [.. run.Lines.Where(line => !line.Contains("backed up", StringComparison.Ordinal))]
        );
        string written = File.ReadAllText(file);
        Assert.Contains("legacy_text='\"old\"'", written, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyText", written, StringComparison.Ordinal);
    }

    [Fact]
    public void OneBadFileChangesNothing()
    {
        WriteUpgradeProject("a.xscn", IgnoreScene);
        Write("b.xscn", IgnoreScene);
        string bad = Write("c.xscn", WithMarker(SkipScene, UpgradeFrom));
        string[] folders = AllFolders();
        Dictionary<string, byte[]> before = Snapshot(AllFiles());

        RunResult run = RunUpgrade("--from", UpgradeTo, "--to", UpgradeTo, folder);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"{bad}: error GXT0006: x:Godot is {UpgradeFrom} but --from is {UpgradeTo}", run.Lines);
        Assert.DoesNotContain(run.Lines, line => line.EndsWith(": upgraded", StringComparison.Ordinal));
        Assert.Equal(folders, AllFolders());
        Assert.Equal(before.Keys, AllFiles());
        AssertUnchanged(before);
        Assert.False(Directory.Exists(Located(StagingFolder)));
        Assert.Empty(BackupFolders());
    }

    [Fact]
    public void CheckWritesNothing()
    {
        string file = WriteUpgradeProject("main.xscn", WithMarker(SkipScene, UpgradeFrom));
        byte[] before = File.ReadAllBytes(file);

        RunResult check = RunUpgrade("--to", UpgradeTo, "--check", folder);

        Assert.Equal(1, check.ExitCode);
        Assert.Equal([$"{file}: would upgrade", "1 of 1 file(s) would upgrade, 0 warning(s)"], check.Lines);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.Empty(BackupFolders());
        Assert.False(Directory.Exists(Located(StagingFolder)));

        Assert.Equal(0, RunUpgrade("--to", UpgradeTo, folder).ExitCode);
        RunResult again = RunUpgrade("--to", UpgradeTo, "--check", folder);

        Assert.Equal(0, again.ExitCode);
        Assert.Equal(["0 of 1 file(s) would upgrade, 0 warning(s)"], again.Lines);
    }

    [Fact]
    public void UpgradeBacksUpTheOriginals()
    {
        string first = WriteUpgradeProject("a.xscn", WithMarker(SkipScene, UpgradeFrom));
        string second = Write("ui/b.xscn", WithMarker(SkipScene, UpgradeFrom));
        Dictionary<string, byte[]> before = Snapshot([first, second]);

        RunResult run = RunUpgrade("--to", UpgradeTo, folder);

        Assert.Equal(0, run.ExitCode);
        string backup = Assert.Single(BackupFolders());
        Assert.Equal(
            [Path.Combine(backup, "a.xscn"), Path.Combine(backup, "ui", "b.xscn")],
            Directory.GetFiles(backup, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        );
        AssertBackedUp(backup, before);
        Assert.Contains($"2 file(s) backed up to {backup}", run.Lines);
    }

    [Fact]
    public void AnUncarriedMarkerIsNotUpgraded()
    {
        string file = WriteUpgradeProject("main.xscn", WithMarker(IgnoreScene, "4.9"));
        string[] folders = AllFolders();
        Dictionary<string, byte[]> before = Snapshot(AllFiles());

        RunResult run = RunUpgrade("--to", UpgradeTo, folder);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(
            [
                $"{file}: error GXT0004: written with Godot 4.9's metadata, which this tool does not carry; not upgraded",
                "0 of 1 file(s) upgraded, 0 warning(s)",
            ],
            run.Lines
        );
        Assert.Equal(folders, AllFolders());
        Assert.Equal(before.Keys, AllFiles());
        AssertUnchanged(before);
    }

    [Fact]
    public void AnUncarriedToIsAUsageError()
    {
        WriteUpgradeProject("main.xscn", WithMarker(SkipScene, UpgradeFrom));

        RunResult run = RunUpgrade("--to", "4.9", folder);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal([$"--to 4.9: this tool carries no metadata for Godot 4.9; it carries {UpgradeFrom}, {UpgradeTo}"], run.ErrorLines);
        Assert.Empty(run.Lines);
    }

    // A project.godot at the test's folder, and one file in it.
    private string WriteUpgradeProject(string relativePath, string text)
    {
        Write("project.godot", "config_version=5\n");
        return Write(relativePath, text);
    }

    private static RunResult RunUpgrade(params string[] args) => RunWith(UpgradePair(), ["upgrade", .. args]);

    private static MetadataVersions UpgradePair()
    {
        var from = GodotMetadata.Load(new MemoryStream(Encoding.UTF8.GetBytes(UpgradeFromMetadata.Value.ToJsonString())));
        return new MetadataVersions(
            [UpgradeFrom, UpgradeTo],
            version =>
                version switch
                {
                    UpgradeFrom => from,
                    UpgradeTo => GodotMetadata.LoadBundled(UpgradeTo),
                    _ => throw new ArgumentException($"This test carries no metadata for Godot {version}.", nameof(version)),
                }
        );
    }

    private static JsonObject UpgradeFromMetadataJson()
    {
        JsonObject json = JsonNode.Parse(File.ReadAllText(RepoRoot.PathOf("metadata", "godot-4.7.json")))!.AsObject();
        json["version"] = UpgradeFrom;
        JsonObject classes = json["classes"]!.AsObject();
        JsonArray members = classes["Control"]!.AsObject()["enums"]!.AsObject()["MouseFilter"]!.AsObject()["values"]!.AsArray();
        JsonArray ignore = members.First(member => (string?)member![0] == "MOUSE_FILTER_IGNORE")!.AsArray();
        ignore[0] = "MOUSE_FILTER_SKIP";
        JsonArray properties = classes["Label"]!.AsObject()["properties"]!.AsArray();
        properties.Add(
            new JsonObject
            {
                ["bitfield"] = false,
                ["className"] = "",
                ["default"] = "\"\"",
                ["enumType"] = null,
                ["hint"] = 0,
                ["hintString"] = "",
                ["name"] = "legacy_text",
                ["type"] = "String",
                ["usage"] = 6,
            }
        );
        return json;
    }
}
