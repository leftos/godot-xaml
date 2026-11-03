using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Cli.Tests;

/// <summary>The <c>convert</c> command, over resources.</summary>
public sealed partial class CliProgramTests
{
    private const string Material = "[gd_resource type=\"CanvasItemMaterial\" format=3]\n\n[resource]\nblend_mode = 1\n";

    [Fact]
    public void ConvertWritesAnXresBesideATres()
    {
        string tres = Write("mat.tres", Material);
        string xres = Path.ChangeExtension(tres, ".xres");

        RunResult run = RunConvert(tres);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"{tres} -> {xres}", "1 converted, 0 refused, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal("", run.Error);
        Assert.Equal(XresOf(Material), File.ReadAllText(xres));
        Assert.Equal(Material, File.ReadAllText(tres));
        Assert.False(File.Exists(xres + ".uid"));
    }

    [Fact]
    public void ConvertWritesATresBesideAnXres()
    {
        string xresText = XresOf(Material);
        string xres = Write("mat.xres", xresText);
        string tres = Path.ChangeExtension(xres, ".tres");

        RunResult run = RunConvert(xres);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"{xres} -> {tres}", "1 converted, 0 refused, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal(TresOf(xresText, uid: null), File.ReadAllText(tres));
        Assert.Equal(xresText, File.ReadAllText(xres));
    }

    [Fact]
    public void ConvertRefusesAnExistingOutput()
    {
        string tres = Write("mat.tres", Material);
        string xres = Write("mat.xres", "old");

        RunResult run = RunConvert(tres);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal([$"{tres}: {xres} exists (use --force)", "0 converted, 1 refused, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(xres));
    }

    [Fact]
    public void ConvertForceOverwritesAnExistingOutput()
    {
        string tres = Write("mat.tres", Material);
        string xres = Write("mat.xres", "old");

        RunResult run = RunConvert("--force", tres);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"{tres} -> {xres}", "1 converted, 0 refused, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal(XresOf(Material), File.ReadAllText(xres));
    }

    [Fact]
    public void ConvertOverwritesWhenTheConfigAllowsIt()
    {
        Write("godot-xaml.json", "{\"convert\": {\"overwrite\": true}}");
        Write("project.godot", "");
        string tres = Write("sub/mat.tres", Material);
        string xres = Write("sub/mat.xres", "old");

        RunResult run = RunIn(Path.Combine(folder, "sub"), "convert", tres);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"{tres} -> {xres}", "1 converted, 0 refused, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal(XresOf(Material), File.ReadAllText(xres));
    }

    [Fact]
    public void ConvertConfigWithAnUnknownKeyIsAUsageError()
    {
        string config = Write("godot-xaml.json", "{\"convert\": {\"overwrite\": true, \"x\": 1}}");
        string tres = Write("mat.tres", Material);

        RunResult run = RunIn(folder, "convert", tres);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal([$"{config}: unknown key 'convert.x'."], run.ErrorLines);
        Assert.False(File.Exists(Path.ChangeExtension(tres, ".xres")));
    }

    [Fact]
    public void ConvertConfigThatIsNotJsonIsAUsageError()
    {
        string config = Write("godot-xaml.json", "not json");
        string tres = Write("mat.tres", Material);

        RunResult run = RunIn(folder, "convert", tres);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        string line = Assert.Single(run.ErrorLines);
        Assert.StartsWith($"{config}: not JSON: ", line, StringComparison.Ordinal);
        Assert.EndsWith(".", line, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.ChangeExtension(tres, ".xres")));
    }

    [Fact]
    public void ConvertConfigOverwriteNotABooleanIsAUsageError()
    {
        string config = Write("godot-xaml.json", "{\"convert\": {\"overwrite\": \"yes\"}}");
        string tres = Write("mat.tres", Material);

        RunResult run = RunIn(folder, "convert", tres);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal([$"{config}: 'convert.overwrite' must be true or false."], run.ErrorLines);
        Assert.False(File.Exists(Path.ChangeExtension(tres, ".xres")));
    }

    [Fact]
    public void ConvertWithAnErrorIssueWritesNothing()
    {
        const string WithUnknownAttribute = "[gd_resource type=\"CanvasItemMaterial\" format=3 foo=1]\n\n[resource]\nblend_mode = 1\n";
        string tres = Write("mat.tres", WithUnknownAttribute);
        ConversionIssue issue = Assert.Single(
            TscnToXscn.Convert(TscnReader.Read(WithUnknownAttribute), ShippedMetadata.Value, new ConversionOptions()).Issues
        );

        RunResult run = RunConvert(tres);

        string[] expected = [$"{tres}: error GXC0001: {issue.Message} ({issue.Location})", "0 converted, 0 refused, 1 error(s), 0 warning(s)"];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.False(File.Exists(Path.ChangeExtension(tres, ".xres")));
    }

    [Fact]
    public void ConvertMovesATresHeaderUidToTheSidecar()
    {
        string tres = Write("mat.tres", "[gd_resource type=\"CanvasItemMaterial\" format=3 uid=\"uid://abc123\"]\n\n[resource]\nblend_mode = 1\n");
        string xres = Path.ChangeExtension(tres, ".xres");

        RunResult run = RunConvert(tres);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("uid://abc123\n", File.ReadAllText(xres + ".uid"));
        Assert.DoesNotContain("abc123", File.ReadAllText(xres), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertPutsTheSidecarUidInTheTresHeader()
    {
        string xresText = XresOf(Material);
        string xres = Write("mat.xres", xresText);
        Write("mat.xres.uid", "uid://abc123\n");
        string tres = Path.ChangeExtension(xres, ".tres");

        RunResult run = RunConvert(xres);

        Assert.Equal(0, run.ExitCode);
        string written = File.ReadAllText(tres);
        Assert.Equal(TresOf(xresText, "uid://abc123"), written);
        Assert.Contains("uid=\"uid://abc123\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertReportsAMalformedTscn()
    {
        const string Malformed = "[gd_resource type=\"CanvasItemMaterial\" format=3\n";
        string tres = Write("mat.tres", Malformed);
        TscnParseException exception = Assert.Throws<TscnParseException>(() => TscnReader.Read(Malformed));

        RunResult run = RunConvert(tres);

        string[] expected =
        [
            $"{tres}({exception.Line},{exception.Column}): error GXT0001: {exception.Reason}",
            "0 converted, 0 refused, 1 error(s), 0 warning(s)",
        ];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.False(File.Exists(Path.ChangeExtension(tres, ".xres")));
    }

    [Fact]
    public void ConvertRefusesAnUnknownExtension()
    {
        string file = Write("notes.txt", "hello");
        string[] expected = [$"{file}: only .tscn, .tres, .xscn and .xres files can be converted."];

        RunResult run = RunConvert(file);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal(expected, run.ErrorLines);
    }

    [Fact]
    public void ConvertReportsAFileTheCoreCannotConvertAndGoesOn()
    {
        string tres = Write("b.tres", Material);
        string tscn = Write(
            "a.tscn",
            "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Node\"]\n\n[node name=\"A\u0001B\" type=\"Node\" parent=\".\"]\n"
        );
        string xres = Path.ChangeExtension(tres, ".xres");

        RunResult run = RunConvert(folder);

        string[] expected =
        [
            $"{tscn}: error GXT0002: The Name attribute of <Node> holds U+0001, which XML 1.0 cannot hold.",
            $"{tres} -> {xres}",
            "1 converted, 0 refused, 1 error(s), 0 warning(s)",
        ];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.Equal("", run.Error);
        Assert.Equal(XresOf(Material), File.ReadAllText(xres));
        Assert.False(File.Exists(Path.ChangeExtension(tscn, ".xscn")));
    }

    [Fact]
    public void WriteFailureLeavesNoTempFile()
    {
        string tres = Write("mat.tres", Material);
        string xres = Path.ChangeExtension(tres, ".xres");
        Directory.CreateDirectory(xres);

        RunResult run = RunConvert("--force", tres);

        Assert.Equal(2, run.ExitCode);
        Assert.StartsWith($"{xres}:", Assert.Single(run.ErrorLines), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(folder, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void SidecarIsRestoredWhenTheOutputWriteFails()
    {
        string tres = Write("mat.tres", "[gd_resource type=\"CanvasItemMaterial\" format=3 uid=\"uid://new123\"]\n\n[resource]\nblend_mode = 1\n");
        string xres = Path.ChangeExtension(tres, ".xres");
        string sidecar = Write("mat.xres.uid", "uid://old\n");
        Directory.CreateDirectory(xres);

        RunResult run = RunConvert("--force", tres);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("uid://old\n", File.ReadAllText(sidecar));
    }

    [Fact]
    public void WritingThroughASymlinkKeepsTheLink()
    {
        string real = Write("real.xscn", NonCanonicalScene);
        string link = Path.Combine(folder, "link.xscn");
        try
        {
            File.CreateSymbolicLink(link, real);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // Creating a symlink needs a privilege (on Windows, Developer Mode or an elevated process) this run lacks.
            Assert.Skip($"symlinks cannot be created here: {exception.Message}");
        }

        RunResult run = Run("format", link);

        Assert.Equal(0, run.ExitCode);
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal(CanonicalScene(NonCanonicalScene), File.ReadAllText(real));
    }

    [Fact]
    public void ConvertDoesNotSwapAPairInOneRun()
    {
        string tres = Write("m.tres", Material);
        string xres = Write("m.xres", XresOf(Material));
        byte[] tresBefore = File.ReadAllBytes(tres);
        byte[] xresBefore = File.ReadAllBytes(xres);

        RunResult run = RunConvert("--force", folder);

        string[] expected =
        [
            $"{tres}: {xres} is also being converted",
            $"{xres}: {tres} is also being converted",
            "0 converted, 2 refused, 0 error(s), 0 warning(s)",
        ];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.Equal(tresBefore, File.ReadAllBytes(tres));
        Assert.Equal(xresBefore, File.ReadAllBytes(xres));
    }

    [Fact]
    public void ConvertRefusesATresHoldingAScene()
    {
        string tres = Write("scene.tres", "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Control\"]\n");

        RunResult run = RunConvert(tres);

        string[] expected = [$"{tres}: error GXT0003: a .tres file holds a scene.", "0 converted, 0 refused, 1 error(s), 0 warning(s)"];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.False(File.Exists(Path.ChangeExtension(tres, ".xres")));
    }

    [Fact]
    public void WritesLeaveNoTempFiles()
    {
        string tres = Write("mat.tres", Material);
        string scene = Write("root.xscn", NonCanonicalScene);

        RunResult converted = RunConvert(tres);
        RunResult formatted = Run("format", scene);

        Assert.Equal(0, converted.ExitCode);
        Assert.Equal(0, formatted.ExitCode);
        string[] names = [.. Directory.EnumerateFiles(folder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];
        Assert.Equal(["godot-xaml.json", "mat.tres", "mat.xres", "project.godot", "root.xscn"], names);
    }

    [Fact]
    public void ConvertRefusesAStaleSidecarAlone()
    {
        string tres = Write("mat.tres", Material);
        string sidecar = Write("mat.xres.uid", "uid://old\n");

        RunResult run = RunConvert(tres);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal([$"{tres}: {sidecar} exists (use --force)", "0 converted, 1 refused, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal("uid://old\n", File.ReadAllText(sidecar));
        Assert.False(File.Exists(Path.ChangeExtension(tres, ".xres")));
    }

    [Fact]
    public void ConvertWithForceRemovesAStaleSidecar()
    {
        string tres = Write("mat.tres", Material);
        string sidecar = Write("mat.xres.uid", "uid://old\n");

        RunResult run = RunConvert("--force", tres);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(XresOf(Material), File.ReadAllText(Path.ChangeExtension(tres, ".xres")));
        Assert.False(File.Exists(sidecar));
    }

    [Fact]
    public void ConvertSeedFollowsTheResPath()
    {
        string xresText = XresOf(GradientTexture);
        string xres = Write("grad.xres", xresText);
        string tres = Path.ChangeExtension(xres, ".tres");

        RunResult run = RunConvert(xres);

        string written = File.ReadAllText(tres);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(TresOf(xresText, new ConversionOptions { Seed = ConvertCommand.SeedOf("res://grad.tres") }, XscnDocumentKind.Resource), written);
        Assert.NotEqual(TresOf(xresText, new ConversionOptions(), XscnDocumentKind.Resource), written);
    }

    [Fact]
    public void ConvertGivesTheSameIdsInTwoCheckouts()
    {
        string xresText = XresOf(GradientTexture);
        string[] outputs = new string[2];
        foreach ((string checkout, int index) in new[] { ("a", 0), ("b", 1) })
        {
            Write(Path.Combine(checkout, "project.godot"), "");
            string xres = Write(Path.Combine(checkout, "ui", "grad.xres"), xresText);

            RunResult run = RunConvert(xres);

            Assert.Equal(0, run.ExitCode);
            outputs[index] = File.ReadAllText(Path.ChangeExtension(xres, ".tres"));
        }
        Assert.Equal(outputs[0], outputs[1]);
        Assert.NotEqual(TresOf(xresText, new ConversionOptions(), XscnDocumentKind.Resource), outputs[0]);
    }

    [Fact]
    public void ConvertWithoutAProjectSeedsFromTheFullPath()
    {
        Write("godot-xaml.json", "{}");
        string xresText = XresOf(GradientTexture);
        string xres = Write("grad.xres", xresText);
        string tres = Path.ChangeExtension(xres, ".tres");

        RunResult run = RunIn(folder, "convert", xres);

        string[] expected =
        [
            $"{xres}: warning GXP0001: no project.godot above this file; its res:// references are not rewritten",
            $"{xres} -> {tres}",
            "1 converted, 0 refused, 0 error(s), 1 warning(s)",
        ];
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.Equal(
            TresOf(xresText, new ConversionOptions { Seed = ConvertCommand.SeedOf(Path.GetFullPath(tres)) }, XscnDocumentKind.Resource),
            File.ReadAllText(tres)
        );
    }

    [Fact]
    public void ConvertSeedUsesOnDiskCasing()
    {
        string xresText = XresOf(GradientTexture);
        Write(Path.Combine("UI", "grad.xres"), xresText);
        string typed = Path.Combine(folder, "ui", "Grad.xres");
        if (!File.Exists(typed))
        {
            Assert.Skip("the file system is case-sensitive, so ui/Grad.xres does not name the file on disk");
        }

        RunResult run = RunConvert(typed);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            TresOf(xresText, new ConversionOptions { Seed = ConvertCommand.SeedOf("res://UI/grad.tres") }, XscnDocumentKind.Resource),
            File.ReadAllText(Path.ChangeExtension(typed, ".tres"))
        );
    }

    [Fact]
    public void SeedOfHashesTheKeyAsGiven()
    {
        // FNV-1a 64 over the UTF-8 of the key, worked out by hand; the key is not resolved against the disk.
        Assert.Equal(0xCFD718F3E18316CDUL, ConvertCommand.SeedOf(@"C:\m.tres"));
        Assert.Equal(0x295E7EB5CBE94B93UL, ConvertCommand.SeedOf("/m.tres"));
    }

    [Fact]
    public void ConvertFolderCollectsAllFourExtensions()
    {
        string tres = Write("a.tres", Material);
        string xres = Write("b.xres", XresOf(Material));
        string tscn = Write("c.tscn", Scene);
        string xscn = Write("d.xscn", XresOf(Scene));
        Write("notes.txt", "hello");

        RunResult run = RunConvert(folder);

        string[] expected =
        [
            $"{tres} -> {Path.ChangeExtension(tres, ".xres")}",
            $"{xres} -> {Path.ChangeExtension(xres, ".tres")}",
            $"{tscn} -> {Path.ChangeExtension(tscn, ".xscn")}",
            $"{xscn} -> {Path.ChangeExtension(xscn, ".tscn")}",
            "4 converted, 0 refused, 0 error(s), 0 warning(s)",
        ];
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.Equal(XresOf(Scene), File.ReadAllText(Path.ChangeExtension(tscn, ".xscn")));
    }

    [Fact]
    public void ConvertWritesAnXscnBesideATscn()
    {
        string tscn = Write("main.tscn", Scene);
        string xscn = Path.ChangeExtension(tscn, ".xscn");

        RunResult run = RunConvert(tscn);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"{tscn} -> {xscn}", "1 converted, 0 refused, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal("", run.Error);
        Assert.Equal(XresOf(Scene), File.ReadAllText(xscn));
        Assert.Equal(Scene, File.ReadAllText(tscn));
    }

    [Fact]
    public void ConvertWritesATscnBesideAnXscn()
    {
        string xscnText = XresOf(Scene);
        string xscn = Write("main.xscn", xscnText);
        string tscn = Path.ChangeExtension(xscn, ".tscn");

        RunResult run = RunConvert(xscn);

        var options = new ConversionOptions { Seed = ConvertCommand.SeedOf("res://main.tscn") };
        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"{xscn} -> {tscn}", "1 converted, 0 refused, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Equal(TresOf(xscnText, options, XscnDocumentKind.Scene), File.ReadAllText(tscn));
        Assert.Equal(xscnText, File.ReadAllText(xscn));
    }

    // A scene with a child and a connection.
    private const string Scene =
        "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Control\"]\n\n[node name=\"Play\" type=\"Button\" parent=\".\"]\n\n"
        + "[connection signal=\"pressed\" from=\"Play\" to=\".\" method=\"_on_play_pressed\"]\n";

    [Fact]
    public void ConvertForceWinsOverOverwriteFalse()
    {
        Write("godot-xaml.json", "{\"convert\": {\"overwrite\": false}}");
        Write("project.godot", "");
        string tres = Write("mat.tres", Material);
        string xres = Write("mat.xres", "old");

        RunResult run = RunIn(folder, "convert", "--force", tres);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(XresOf(Material), File.ReadAllText(xres));
    }

    private const string GradientTexture =
        "[gd_resource type=\"GradientTexture1D\" format=3]\n\n[sub_resource type=\"Gradient\" id=\"Gradient_abc12\"]\n"
        + "offsets = PackedFloat32Array(0, 1)\n\n[resource]\ngradient = SubResource(\"Gradient_abc12\")\n";

    // A run with an empty godot-xaml.json in the test's folder, so a config above the temp folder is never found, and a
    // project root there unless the test wrote its own, so the run's files belong to a Godot project.
    private RunResult RunConvert(params string[] args)
    {
        Write("godot-xaml.json", "{}");
        if (!File.Exists(Path.Combine(folder, "project.godot")))
        {
            Write("project.godot", "");
        }
        return RunIn(folder, ["convert", .. args]);
    }

    // The forward conversion reads no option, so the expected text needs none.
    private static string XresOf(string tres)
    {
        GodotMetadata metadata = ShippedMetadata.Value;
        return XscnWriter.Write(TscnToXscn.Convert(TscnReader.Read(tres), metadata, new ConversionOptions()).Document, metadata);
    }

    // The material holds no sub or ext resource, so no id is drawn from the seed and the default one gives the same text.
    private static string TresOf(string xres, string? uid) => TresOf(xres, new ConversionOptions { Uid = uid }, XscnDocumentKind.Resource);

    private static string TresOf(string xres, ConversionOptions options, XscnDocumentKind kind)
    {
        GodotMetadata metadata = ShippedMetadata.Value;
        XscnDocument document = XscnParser.Parse(xres, kind, metadata).Document;
        return TscnWriter.Write(XscnToTscn.Convert(document, metadata, options).Document);
    }

    private static RunResult RunIn(string workingDirectory, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = CliProgram.Run(args, output, error, path => Directory.EnumerateFiles(path), workingDirectory);
        return new RunResult(exitCode, output.ToString(), error.ToString());
    }
}
