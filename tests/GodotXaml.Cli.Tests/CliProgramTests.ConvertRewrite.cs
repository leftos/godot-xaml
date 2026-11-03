using GodotXaml.Core.Tscn;

namespace GodotXaml.Cli.Tests;

/// <summary>The <c>convert</c> command rewriting the references between the files one run converts, and warning about the rest.</summary>
public sealed partial class CliProgramTests
{
    private const string Namespace = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";
    private const string Project = "config_version=5\n";
    private const string ButtonScene = "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Button\"]\n";

    [Fact]
    public void ConvertRewritesAnInstanceOfAnotherConvertedScene()
    {
        Write("project.godot", Project);
        string button = Write("ui/button.tscn", "[gd_scene format=3 uid=\"uid://bbutt\"]\n\n[node name=\"Root\" type=\"Button\"]\n");
        string main = Write("main.tscn", Instancing("res://ui/button.tscn", "uid://bbutt"));
        string mainXscn = Path.ChangeExtension(main, ".xscn");

        RunResult run = RunConvert(folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{main} -> {mainXscn}",
                $"{button} -> {Path.ChangeExtension(button, ".xscn")}",
                $"{button}: warning GXP0003: the source still carries uid://bbutt, which now belongs to button.xscn; "
                    + "remove the source or Godot will give one of them a new uid",
                "2 converted, 0 refused, 0 error(s), 1 warning(s)",
            ],
            run.Lines
        );
        string written = File.ReadAllText(mainXscn);
        Assert.Contains("<ui:button", written, StringComparison.Ordinal);
        Assert.Contains("Path=\"res://ui/button.xscn\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("res://ui/button.tscn", written, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertRewritesAResourceFileProperty()
    {
        Write("project.godot", Project);
        string theme = Write("themes/t.tres", "[gd_resource type=\"Theme\" format=3]\n\n[resource]\ndefault_font_size = 12\n");
        string main = Write(
            "main.tscn",
            "[gd_scene load_steps=2 format=3]\n\n[ext_resource type=\"Theme\" path=\"res://themes/t.tres\" id=\"1_t\"]\n\n"
                + "[node name=\"Root\" type=\"Control\"]\ntheme = ExtResource(\"1_t\")\n"
        );

        RunResult run = RunConvert(folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{main} -> {Path.ChangeExtension(main, ".xscn")}",
                $"{theme} -> {Path.ChangeExtension(theme, ".xres")}",
                "2 converted, 0 refused, 0 error(s), 0 warning(s)",
            ],
            run.Lines
        );
        string written = File.ReadAllText(Path.ChangeExtension(main, ".xscn"));
        Assert.Contains("res://themes/t.xres", written, StringComparison.Ordinal);
        Assert.DoesNotContain("res://themes/t.tres", written, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertKeepsAReferenceToAFileNotConverted()
    {
        Write("project.godot", Project);
        Write("ui/button.tscn", ButtonScene);
        string main = Write("main.tscn", Instancing("res://ui/button.tscn"));

        RunResult run = RunConvert(main);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"{main} -> {Path.ChangeExtension(main, ".xscn")}", "1 converted, 0 refused, 0 error(s), 0 warning(s)"], run.Lines);
        Assert.Contains("res://ui/button.tscn", File.ReadAllText(Path.ChangeExtension(main, ".xscn")), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertBackRewritesReferencesBeforeConverting()
    {
        Write("project.godot", Project);
        string button = Write("ui/button.xscn", $"<Button {Namespace} Name=\"Root\" />");
        string main = Write(
            "main.xscn",
            $"<Control {Namespace} xmlns:ui=\"res://ui/\" Name=\"Root\">\n"
                + "  <x:Instance Name=\"Button\" Scene=\"res://ui/button.xscn\" />\n</Control>\n"
        );
        string mainTscn = Path.ChangeExtension(main, ".tscn");

        RunResult run = RunConvert(folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [$"{main} -> {mainTscn}", $"{button} -> {Path.ChangeExtension(button, ".tscn")}", "2 converted, 0 refused, 0 error(s), 0 warning(s)"],
            run.Lines
        );
        string written = File.ReadAllText(mainTscn);
        Assert.Contains("path=\"res://ui/button.tscn\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("button.xscn", written, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertDoesNotRewriteForASourceThatFailed()
    {
        Write("project.godot", Project);
        const string Malformed = "[gd_scene format=3\n";
        string button = Write("ui/button.tscn", Malformed);
        string main = Write("main.tscn", Instancing("res://ui/button.tscn"));
        TscnParseException exception = Assert.Throws<TscnParseException>(() => TscnReader.Read(Malformed));

        RunResult run = RunConvert(folder);

        string[] expected =
        [
            $"{button}({exception.Line},{exception.Column}): error GXT0001: {exception.Reason}",
            $"{main} -> {Path.ChangeExtension(main, ".xscn")}",
            "1 converted, 0 refused, 1 error(s), 0 warning(s)",
        ];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.Contains("res://ui/button.tscn", File.ReadAllText(Path.ChangeExtension(main, ".xscn")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConvertWarnsAboutAReferenceFromAnUntouchedScene(bool withUid)
    {
        Write("project.godot", Project);
        string button = Write("ui/button.tscn", ButtonScene);
        string resource = withUid
            ? "[ext_resource type=\"PackedScene\" uid=\"uid://bbutt\" path=\"res://ui/button.tscn\" id=\"1_b\"]"
            : "[ext_resource type=\"PackedScene\" path=\"res://ui/button.tscn\" id=\"1_b\"]";
        string other = Write(
            "other.tscn",
            $"[gd_scene load_steps=2 format=3]\n\n{resource}\n\n[node name=\"Other\" type=\"Control\"]\n\n"
                + "[node name=\"Button\" parent=\".\" instance=ExtResource(\"1_b\")]\n"
        );
        int column = resource.IndexOf("\"res://ui/button.tscn\"", StringComparison.Ordinal) + 1;
        string clause = withUid
            ? "still resolves by its uid once the source is removed and the uid moves with the file"
            : "no uid on this reference; it breaks once the source is removed";

        RunResult run = RunConvert(button);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{button} -> {Path.ChangeExtension(button, ".xscn")}",
                $"{other}(3,{column}): warning GXP0002: refers to res://ui/button.tscn, which this run converts to "
                    + $"res://ui/button.xscn; {clause}",
                "1 converted, 0 refused, 0 error(s), 1 warning(s)",
            ],
            run.Lines
        );
    }

    [Fact]
    public void ConvertWarnsOnceWithoutAProjectRoot()
    {
        string scene = Write("main.xscn", $"<Control {Namespace} Name=\"Root\" />");
        string tres = Write("mat.tres", Material);
        Write("godot-xaml.json", "{}");

        RunResult run = RunIn(folder, "convert", folder);

        string[] expected =
        [
            $"{scene}: warning GXP0001: no project.godot above this file; its res:// references are not rewritten",
            $"{tres}: warning GXP0001: no project.godot above this file; its res:// references are not rewritten",
            $"{scene} -> {Path.ChangeExtension(scene, ".tscn")}",
            $"{tres} -> {Path.ChangeExtension(tres, ".xres")}",
            "2 converted, 0 refused, 0 error(s), 2 warning(s)",
        ];
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expected, run.Lines);
    }

    [Fact]
    public void ConvertWarnsThatTheSourceKeepsItsUid()
    {
        Write("project.godot", Project);
        string tres = Write("mat.tres", "[gd_resource type=\"CanvasItemMaterial\" format=3 uid=\"uid://abc123\"]\n\n[resource]\nblend_mode = 1\n");
        string xres = Path.ChangeExtension(tres, ".xres");

        RunResult run = RunConvert(tres);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{tres} -> {xres}",
                $"{tres}: warning GXP0003: the source still carries uid://abc123, which now belongs to mat.xres; "
                    + "remove the source or Godot will give one of them a new uid",
                "1 converted, 0 refused, 0 error(s), 1 warning(s)",
            ],
            run.Lines
        );
    }

    [Fact]
    public void ConvertRewritesPerProjectRoot()
    {
        Write("a/project.godot", Project);
        Write("b/project.godot", Project);
        string button = Write("a/ui/button.tscn", ButtonScene);
        string aMain = Write("a/main.tscn", Instancing("res://ui/button.tscn"));
        string bMain = Write("b/main.tscn", Instancing("res://ui/button.tscn"));

        RunResult run = RunConvert(folder);

        string aWritten = File.ReadAllText(Path.ChangeExtension(aMain, ".xscn"));
        string bWritten = File.ReadAllText(Path.ChangeExtension(bMain, ".xscn"));
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{aMain} -> {Path.ChangeExtension(aMain, ".xscn")}",
                $"{button} -> {Path.ChangeExtension(button, ".xscn")}",
                $"{bMain} -> {Path.ChangeExtension(bMain, ".xscn")}",
                "3 converted, 0 refused, 0 error(s), 0 warning(s)",
            ],
            run.Lines
        );
        Assert.Contains("<ui:button", aWritten, StringComparison.Ordinal);
        Assert.Contains("res://ui/button.tscn", bWritten, StringComparison.Ordinal);
        Assert.DoesNotContain("<ui:button", bWritten, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertWarnsAboutAReferenceFromASourceThatFailed()
    {
        Write("project.godot", Project);
        const string Reference = "[ext_resource type=\"PackedScene\" path=\"res://ui/button.tscn\" id=\"1_b\"]";
        const string Broken = "[gd_scene load_steps=2 format=3]\n\n" + Reference + "\n\n[node name=\"Broken\" type=\"Control\"\n";
        string broken = Write("broken.tscn", Broken);
        string button = Write("ui/button.tscn", ButtonScene);
        TscnParseException exception = Assert.Throws<TscnParseException>(() => TscnReader.Read(Broken));
        int column = Reference.IndexOf("\"res://ui/button.tscn\"", StringComparison.Ordinal) + 1;

        RunResult run = RunConvert(folder);

        string[] expected =
        [
            $"{broken}({exception.Line},{exception.Column}): error GXT0001: {exception.Reason}",
            $"{button} -> {Path.ChangeExtension(button, ".xscn")}",
            $"{broken}(3,{column}): warning GXP0002: refers to res://ui/button.tscn, which this run converts to "
                + "res://ui/button.xscn; no uid on this reference; it breaks once the source is removed",
            "1 converted, 0 refused, 1 error(s), 1 warning(s)",
        ];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
    }

    [Fact]
    public void ConvertDropsASourceTheWriterRefusesBeforeRewriting()
    {
        Write("project.godot", Project);
        string bad = Write(
            "ui/bad.tscn",
            "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Node\"]\n\n[node name=\"A\u0001B\" type=\"Node\" parent=\".\"]\n"
        );
        string main = Write("main.tscn", Instancing("res://ui/bad.tscn"));

        RunResult run = RunConvert(folder);

        string[] expected =
        [
            $"{bad}: error GXT0002: The Name attribute of <Node> holds U+0001, which XML 1.0 cannot hold.",
            $"{main} -> {Path.ChangeExtension(main, ".xscn")}",
            "1 converted, 0 refused, 1 error(s), 0 warning(s)",
        ];
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.Contains("res://ui/bad.tscn", File.ReadAllText(Path.ChangeExtension(main, ".xscn")), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertWarnsAboutAReferenceToAnOutputThatFailedToWrite()
    {
        Write("project.godot", Project);
        string theme = Write("themes/t.tres", "[gd_resource type=\"Theme\" format=3]\n\n[resource]\ndefault_font_size = 12\n");
        string main = Write(
            "main.tscn",
            "[gd_scene load_steps=2 format=3]\n\n[ext_resource type=\"Theme\" path=\"res://themes/t.tres\" id=\"1_t\"]\n\n"
                + "[node name=\"Root\" type=\"Control\"]\ntheme = ExtResource(\"1_t\")\n"
        );
        string mainXscn = Path.ChangeExtension(main, ".xscn");
        string themeXres = Path.ChangeExtension(theme, ".xres");
        Directory.CreateDirectory(themeXres);

        RunResult run = RunConvert(folder);

        string[] written = File.ReadAllText(mainXscn).Split('\n');
        int line = Array.FindIndex(written, text => text.Contains("\"res://themes/t.xres\"", StringComparison.Ordinal)) + 1;
        int column = written[line - 1].IndexOf("\"res://themes/t.xres\"", StringComparison.Ordinal) + 1;
        string[] expected =
        [
            $"{main} -> {mainXscn}",
            $"{mainXscn}({line},{column}): warning GXP0005: refers to res://themes/t.xres, which this run failed to write; "
                + "res://themes/t.tres still exists",
            "1 converted, 0 refused, 1 error(s), 1 warning(s)",
        ];
        Assert.Equal(2, run.ExitCode);
        Assert.Equal(expected, run.Lines);
        Assert.StartsWith($"{themeXres}: ", Assert.Single(run.ErrorLines), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertUsesTheOnDiskCasingForResPaths()
    {
        Write("project.godot", Project);
        Write("ui/button.tscn", ButtonScene);
        string main = Write("main.tscn", Instancing("res://ui/button.tscn"));
        string typed = Path.Combine(folder, "UI", "Button.tscn");
        if (!File.Exists(typed))
        {
            Assert.Skip("the file system is case-sensitive, so UI/Button.tscn does not name the file on disk");
        }

        RunResult run = RunConvert(typed, main);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{typed} -> {Path.ChangeExtension(typed, ".xscn")}",
                $"{main} -> {Path.ChangeExtension(main, ".xscn")}",
                "2 converted, 0 refused, 0 error(s), 0 warning(s)",
            ],
            run.Lines
        );
        string written = File.ReadAllText(Path.ChangeExtension(main, ".xscn"));
        Assert.DoesNotContain(".tscn", written, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertDoesNotScanANestedProject()
    {
        Write("project.godot", Project);
        Write("nested/project.godot", Project);
        string button = Write("ui/button.tscn", ButtonScene);
        Write(
            "nested/other.tscn",
            "[gd_scene load_steps=2 format=3]\n\n[ext_resource type=\"PackedScene\" path=\"res://ui/button.tscn\" id=\"1_b\"]\n\n"
                + "[node name=\"Other\" type=\"Control\"]\n\n[node name=\"Button\" parent=\".\" instance=ExtResource(\"1_b\")]\n"
        );

        RunResult run = RunConvert(button);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([$"{button} -> {Path.ChangeExtension(button, ".xscn")}", "1 converted, 0 refused, 0 error(s), 0 warning(s)"], run.Lines);
    }

    [Fact]
    public void ConvertWarnsNoUidWhenTheLinesUidBelongsToAnotherReference()
    {
        Write("project.godot", Project);
        string button = Write("ui/button.tscn", ButtonScene);
        const string Property =
            "<x:Prop Name=\"metadata/both\" Value='[Resource(\"uid://aother\", \"res://ui/other.tres\"), "
            + "Resource(\"res://ui/button.tscn\")]' />";
        string line = $"  {Property}";
        string other = Write("other.xscn", $"<Control {Namespace} Name=\"Root\">\n{line}\n</Control>\n");
        int column = line.IndexOf("\"res://ui/button.tscn\"", StringComparison.Ordinal) + 1;

        RunResult run = RunConvert(button);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{button} -> {Path.ChangeExtension(button, ".xscn")}",
                $"{other}(2,{column}): warning GXP0002: refers to res://ui/button.tscn, which this run converts to "
                    + "res://ui/button.xscn; no uid on this reference; it breaks once the source is removed",
                "1 converted, 0 refused, 0 error(s), 1 warning(s)",
            ],
            run.Lines
        );
    }

    [Fact]
    public void ConvertWarnsAboutAnEntityEncodedReference()
    {
        Write("project.godot", Project);
        string button = Write("ui/button.tscn", ButtonScene);
        const string Node = "  <Node Name=\"N\" Value=\"[Resource(&quot;res://ui/button.tscn&quot;), 'x']\" />";
        string other = Write("other.xscn", $"<Control {Namespace} Name=\"Root\">\n{Node}\n</Control>\n");
        int column = Node.IndexOf("&quot;res://ui/button.tscn", StringComparison.Ordinal) + 1;

        RunResult run = RunConvert(button);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{button} -> {Path.ChangeExtension(button, ".xscn")}",
                $"{other}(2,{column}): warning GXP0002: refers to res://ui/button.tscn, which this run converts to "
                    + "res://ui/button.xscn; no uid on this reference; it breaks once the source is removed",
                "1 converted, 0 refused, 0 error(s), 1 warning(s)",
            ],
            run.Lines
        );
    }

    [Fact]
    public void ConvertWarnsTheUidTableCarriesTheReferenceAcrossLines()
    {
        Write("project.godot", Project);
        string button = Write("ui/button.tscn", ButtonScene);
        const string Entry = "      Path=\"res://ui/button.tscn\"";
        const string Value = "      Value=\"uid://bbutt\" />";
        const string Property = "  <x:Prop Name=\"metadata/path\" Value='Resource(\"res://ui/button.tscn\")' />";
        string other = Write(
            "other.xscn",
            $"<Control {Namespace} Name=\"Root\">\n  <x:Uids>\n    <x:Uid\n{Entry}\n{Value}\n  </x:Uids>\n{Property}\n</Control>\n"
        );
        int tableColumn = Entry.IndexOf("\"res://ui/button.tscn\"", StringComparison.Ordinal) + 1;
        int propertyColumn = Property.IndexOf("\"res://ui/button.tscn\"", StringComparison.Ordinal) + 1;
        const string Message =
            "warning GXP0002: refers to res://ui/button.tscn, which this run converts to res://ui/button.xscn; "
            + "still resolves by its uid once the source is removed and the uid moves with the file";

        RunResult run = RunConvert(button);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{button} -> {Path.ChangeExtension(button, ".xscn")}",
                $"{other}(4,{tableColumn}): {Message}",
                $"{other}(7,{propertyColumn}): {Message}",
                "1 converted, 0 refused, 0 error(s), 2 warning(s)",
            ],
            run.Lines
        );
    }

    [Fact]
    public void ConvertWarnsTheUidTableCarriesAnEntityEncodedPath()
    {
        Write("project.godot", Project);
        string resource = Write("a&b.tscn", ButtonScene);
        const string Entry = "    <x:Uid Path=\"res://a&amp;b.tscn\" Value=\"uid://camb\" />";
        string other = Write("other.xscn", $"<Control {Namespace} Name=\"Root\">\n  <x:Uids>\n{Entry}\n  </x:Uids>\n</Control>\n");
        int column = Entry.IndexOf("\"res://a&amp;b.tscn\"", StringComparison.Ordinal) + 1;

        RunResult run = RunConvert(resource);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{resource} -> {Path.ChangeExtension(resource, ".xscn")}",
                $"{other}(3,{column}): warning GXP0002: refers to res://a&b.tscn, which this run converts to "
                    + "res://a&b.xscn; still resolves by its uid once the source is removed and the uid moves with the file",
                "1 converted, 0 refused, 0 error(s), 1 warning(s)",
            ],
            run.Lines
        );
    }

    [Fact]
    public void ConvertWarnsTheUidTableCarriesTheReference()
    {
        Write("project.godot", Project);
        string button = Write("ui/button.tscn", ButtonScene);
        const string Table = "    <x:Uid Path=\"res://ui/button.tscn\" Value=\"uid://bbutt\" />";
        const string Property = "  <x:Prop Name=\"metadata/path\" Value='Resource(\"res://ui/button.tscn\")' />";
        string other = Write("other.xscn", $"<Control {Namespace} Name=\"Root\">\n  <x:Uids>\n{Table}\n  </x:Uids>\n{Property}\n</Control>\n");
        int tableColumn = Table.IndexOf("\"res://ui/button.tscn\"", StringComparison.Ordinal) + 1;
        int propertyColumn = Property.IndexOf("\"res://ui/button.tscn\"", StringComparison.Ordinal) + 1;
        const string Message =
            "warning GXP0002: refers to res://ui/button.tscn, which this run converts to res://ui/button.xscn; "
            + "still resolves by its uid once the source is removed and the uid moves with the file";

        RunResult run = RunConvert(button);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{button} -> {Path.ChangeExtension(button, ".xscn")}",
                $"{other}(3,{tableColumn}): {Message}",
                $"{other}(5,{propertyColumn}): {Message}",
                "1 converted, 0 refused, 0 error(s), 2 warning(s)",
            ],
            run.Lines
        );
    }

    [Fact]
    public void ConvertWarnsAboutAnInstanceTagOfAnOutputThatFailedToWrite()
    {
        Write("project.godot", Project);
        string button = Write("ui/button.tscn", ButtonScene);
        string main = Write("main.tscn", Instancing("res://ui/button.tscn"));
        string mainXscn = Path.ChangeExtension(main, ".xscn");
        string buttonXscn = Path.ChangeExtension(button, ".xscn");
        Directory.CreateDirectory(buttonXscn);

        RunResult run = RunConvert(folder);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(
            [
                $"{main} -> {mainXscn}",
                $"{mainXscn}: warning GXP0005: refers to res://ui/button.xscn, which this run failed to write; "
                    + "res://ui/button.tscn still exists",
                "1 converted, 0 refused, 1 error(s), 1 warning(s)",
            ],
            run.Lines
        );
        Assert.StartsWith($"{buttonXscn}: ", Assert.Single(run.ErrorLines), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertDoesNotSayAFailedWriteWasConverted()
    {
        Write("project.godot", Project);
        string theme = Write("themes/t.tres", "[gd_resource type=\"Theme\" format=3]\n\n[resource]\ndefault_font_size = 12\n");
        Write(
            "other.tscn",
            "[gd_scene load_steps=2 format=3]\n\n[ext_resource type=\"Theme\" path=\"res://themes/t.tres\" id=\"1_t\"]\n\n"
                + "[node name=\"Root\" type=\"Control\"]\ntheme = ExtResource(\"1_t\")\n"
        );
        string themeXres = Path.ChangeExtension(theme, ".xres");
        Directory.CreateDirectory(themeXres);

        RunResult run = RunConvert(theme);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(["0 converted, 0 refused, 1 error(s), 0 warning(s)"], run.Lines);
        Assert.StartsWith($"{themeXres}: ", Assert.Single(run.ErrorLines), StringComparison.Ordinal);
    }

    [Fact]
    public void ResPathPrefersTheExactlyCasedEntry()
    {
        Assert.Equal("UI", ProjectRoot.OnDiskName("UI", ["ui", "UI"]));
        Assert.Equal("ui", ProjectRoot.OnDiskName("ui", ["ui", "UI"]));
        Assert.Equal("ui", ProjectRoot.OnDiskName("Ui", ["ui", "OTHER"]));
        Assert.Equal("Button.tscn", ProjectRoot.OnDiskName("Button.tscn", []));
        Assert.Equal("Button.tscn", ProjectRoot.OnDiskName("Button.tscn", ["other.tscn"]));
    }

    [Fact]
    public void ConvertWarnsAboutAReferenceAfterAnEscapedQuote()
    {
        Write("project.godot", Project);
        string button = Write("ui/button.tscn", ButtonScene);
        const string Paths = "paths = PackedStringArray(\"5\\\" screen\", \"res://ui/button.tscn\")";
        string other = Write("other.tscn", $"[gd_scene format=3]\n\n[node name=\"Root\" type=\"Node\"]\n{Paths}\n");
        int column = Paths.IndexOf("\"res://ui/button.tscn\"", StringComparison.Ordinal) + 1;
        const string Message =
            "warning GXP0002: refers to res://ui/button.tscn, which this run converts to res://ui/button.xscn; "
            + "no uid on this reference; it breaks once the source is removed";

        RunResult run = RunConvert(button);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{button} -> {Path.ChangeExtension(button, ".xscn")}",
                $"{other}(4,{column}): {Message}",
                "1 converted, 0 refused, 0 error(s), 1 warning(s)",
            ],
            run.Lines
        );
    }

    [Fact]
    public void ConvertWarnsAboutAnInstanceAfterANameEndingInABackslash()
    {
        Write("project.godot", Project);
        string button = Write("ui/button.tscn", ButtonScene);
        const string Instance = "  <x:Instance Name=\"Slot\\\" Scene=\"res://ui/button.tscn\" />";
        string other = Write("other.xscn", $"<Control {Namespace} Name=\"Root\">\n{Instance}\n</Control>\n");
        int column = Instance.IndexOf("\"res://ui/button.tscn\"", StringComparison.Ordinal) + 1;
        const string Message =
            "warning GXP0002: refers to res://ui/button.tscn, which this run converts to res://ui/button.xscn; "
            + "no uid on this reference; it breaks once the source is removed";

        RunResult run = RunConvert(button);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{button} -> {Path.ChangeExtension(button, ".xscn")}",
                $"{other}(2,{column}): {Message}",
                "1 converted, 0 refused, 0 error(s), 1 warning(s)",
            ],
            run.Lines
        );
    }

    // A scene whose root node instances another scene, by an ext_resource the test names.
    private static string Instancing(string path, string? uid = null)
    {
        string resource = uid is null ? "" : $" uid=\"{uid}\"";
        return "[gd_scene load_steps=2 format=3]\n\n"
            + $"[ext_resource type=\"PackedScene\"{resource} path=\"{path}\" id=\"1_b\"]\n\n"
            + "[node name=\"Main\" type=\"Control\"]\n\n[node name=\"Button\" parent=\".\" instance=ExtResource(\"1_b\")]\n";
    }
}
