using System.Diagnostics;
using System.Text;

namespace GodotXaml.Cli.Tests;

/// <summary>The <c>convert --project</c> run: every scene and resource of a project converted at once, or nothing changed.</summary>
public sealed partial class CliProgramTests
{
    private const string BackupFolder = ".godot-xaml-backup";
    private const string StagingFolder = ".godot-xaml-staging";
    private const string ThemeResource = "[gd_resource type=\"Theme\" format=3]\n\n[resource]\ndefault_font_size = 12\n";
    private const string BusLayout = "[gd_resource type=\"AudioBusLayout\" format=3]\n\n[resource]\n";
    private const string BrokenScene = "[gd_scene format=3]\n\n[node name=\"Root\"\n";
    private const string MainScript = "extends Node\nconst M = preload(\"res://main.tscn\")\n";
    private const string MainProject = "config_version=5\n\n[application]\nrun/main_scene=\"res://main.tscn\"\n";

    private const string ThemedButton =
        "[gd_scene load_steps=2 format=3]\n\n[ext_resource type=\"Theme\" path=\"res://themes/t.tres\" id=\"1_t\"]\n\n"
        + "[node name=\"Root\" type=\"Button\"]\ntheme = ExtResource(\"1_t\")\n";

    [Fact]
    public void ProjectConvertsEveryScene()
    {
        string[] sources = WriteThreeScenes(ThemedButton);
        Dictionary<string, byte[]> before = Snapshot(sources);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        AssertInstancesTheConvertedButton(File.ReadAllText(Path.Combine(folder, "main.xscn")));
        Assert.Contains("res://themes/t.xres", File.ReadAllText(Path.Combine(folder, "ui", "button.xscn")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "themes", "t.xres")));
        Assert.All(sources, source => Assert.False(File.Exists(source)));
        AssertBackedUp(Assert.Single(BackupFolders()), before);
    }

    [Fact]
    public void ProjectEditsScriptsAndProjectGodot()
    {
        Write("main.tscn", ButtonScene);
        string gd = Write("a.gd", MainScript);
        string cs = Write("b.cs", "class B\n{\n    object M = GD.Load<PackedScene>(\"res://main.tscn\");\n}\n");
        string project = Write("project.godot", MainProject + "\n[autoload]\nMain=\"*res://main.tscn\"\n");
        Dictionary<string, byte[]> before = Snapshot([gd, cs, project]);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        foreach ((string file, byte[] bytes) in before)
        {
            string expected = Encoding.UTF8.GetString(bytes).Replace("res://main.tscn", "res://main.xscn", StringComparison.Ordinal);
            Assert.Equal(expected, File.ReadAllText(file));
        }
        Assert.Contains($"{gd}: 1 path(s) rewritten", run.Lines);
        Assert.Contains($"{cs}: 1 path(s) rewritten", run.Lines);
        Assert.Contains($"{project}: 2 path(s) rewritten", run.Lines);
        Assert.DoesNotContain(run.Lines, line => line.Contains("GXP0002", StringComparison.Ordinal));
        AssertBackedUp(Assert.Single(BackupFolders()), before);
    }

    [Fact]
    public void ProjectKeepsABomAndLineEndings()
    {
        Write("project.godot", Project);
        Write("main.tscn", ButtonScene);
        byte[] bom = [0xEF, 0xBB, 0xBF];
        const string Script = "class A\r\n{\r\n    string P = \"res://main.tscn\";\r\n}\r\n";
        string cs = WriteBytes("a.cs", [.. bom, .. Encoding.UTF8.GetBytes(Script)]);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        byte[] expected = [.. bom, .. Encoding.UTF8.GetBytes(Script.Replace("main.tscn", "main.xscn", StringComparison.Ordinal))];
        Assert.Equal(expected, File.ReadAllBytes(cs));
    }

    [Fact]
    public void ProjectLeavesAUidMainScene()
    {
        const string Godot = "config_version=5\n\n[application]\nrun/main_scene=\"uid://abc\"\n";
        string project = Write("project.godot", Godot);
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(Godot, File.ReadAllText(project));
        Assert.DoesNotContain(run.Lines, line => line.Contains(": warning ", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectSkipsAddons()
    {
        Write("project.godot", Project);
        string plugin = Write("addons/x/plugin.tscn", ButtonScene);
        const string Script = "var p = \"res://main.tscn\"\n";
        string gd = Write("addons/x/plugin.gd", Script);
        Write("main.tscn", Instancing("res://addons/x/plugin.tscn"));

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(plugin));
        Assert.False(File.Exists(Path.ChangeExtension(plugin, ".xscn")));
        Assert.Equal(Script, File.ReadAllText(gd));
        Assert.Contains(run.Lines, line => line.StartsWith($"{gd}(1,9): warning GXP0002", StringComparison.Ordinal));
        Assert.Contains("res://addons/x/plugin.tscn", File.ReadAllText(Path.Combine(folder, "main.xscn")), StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectSaysAddonsAreNotEdited()
    {
        Write("project.godot", Project);
        Write("ui/button.tscn", ButtonScene);
        string gd = Write("addons/x/a.gd", "var p = \"res://ui/\" + name + \".tscn\"\nprint(\"could not load res://ui/button.tscn\")\n");

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(
            $"{gd}(1,30): warning GXP0006: ends in .tscn; this run converts such files to .xscn but edits nothing under addons/, "
                + "so a path built from it is not rewritten",
            run.Lines
        );
        Assert.Contains(
            $"{gd}(2,7): warning GXP0006: holds res://ui/button.tscn, which this run converts to res://ui/button.xscn, "
                + "inside a longer string; addons/ is not edited, so it is not rewritten",
            run.Lines
        );
    }

    [Fact]
    public void ProjectLeavesTheFilesGodotLoadsBeforeTheAddonAsText()
    {
        string project = Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"res://t.tres\"\n");
        byte[] godotBefore = File.ReadAllBytes(project);
        string theme = Write("t.tres", ThemeResource);
        string bus = Write("default_bus_layout.tres", BusLayout);
        Dictionary<string, byte[]> before = Snapshot([Write("main.tscn", ButtonScene)]);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(theme));
        Assert.True(File.Exists(bus));
        Assert.True(File.Exists(Located("main.xscn")));
        Assert.False(File.Exists(Located("t.xres")));
        Assert.False(File.Exists(Located("default_bus_layout.xres")));
        Assert.Contains($"{theme}: left as text: Godot loads it before any addon (gui/theme/custom)", run.Lines);
        Assert.Contains($"{bus}: left as text: Godot loads it before any addon (audio/buses/default_bus_layout)", run.Lines);
        Assert.Equal(godotBefore, File.ReadAllBytes(project));
        Assert.Contains(run.Lines, line => line.EndsWith("0 warning(s)", StringComparison.Ordinal));
        string backup = Assert.Single(BackupFolders());
        AssertBackedUp(backup, before);
        Assert.False(File.Exists(Path.Combine(backup, "t.tres")));
        Assert.False(File.Exists(Path.Combine(backup, "default_bus_layout.tres")));
    }

    [Fact]
    public void ProjectLeavesAFileNamedByABareSettingName()
    {
        Write("project.godot", "config_version=5\ngui/theme/custom=\"res://t.tres\"\n");
        string theme = Write("t.tres", ThemeResource);
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(theme));
        Assert.False(File.Exists(Located("t.xres")));
        Assert.Contains($"{theme}: left as text: Godot loads it before any addon (gui/theme/custom)", run.Lines);
    }

    [Fact]
    public void ProjectLeavesAFileNamedByAFeatureTag()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom.mobile=\"res://m.tres\"\n");
        string theme = Write("m.tres", ThemeResource);
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(theme));
        Assert.False(File.Exists(Located("m.xres")));
        Assert.Contains($"{theme}: left as text: Godot loads it before any addon (gui/theme/custom)", run.Lines);
    }

    [Fact]
    public void ProjectResolvesAUidSetting()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"uid://abc\"\n");
        string theme = Write("t.tres", "[gd_resource type=\"Theme\" format=3 uid=\"uid://abc\"]\n\n[resource]\ndefault_font_size = 12\n");
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(theme));
        Assert.False(File.Exists(Located("t.xres")));
        Assert.Contains($"{theme}: left as text: Godot loads it before any addon (gui/theme/custom)", run.Lines);
    }

    // A source whose header claims a uid and whose body Godot cannot parse does not stand for the uid.
    [Fact]
    public void ABrokenFileClaimingAUidDoesNotHideTheFileThatParses()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"uid://dupe\"\n");
        const string Header = "[gd_resource type=\"Theme\" format=3 uid=\"uid://dupe\"]\n\n[resource]\n";
        string broken = Write("a.tres", Header + "\n[sub_resource type=\"Gradient\"\n");
        string whole = Write("b.tres", Header + "default_font_size = 12\n");

        RunResult run = RunProject();

        string left = $"{whole}: left as text: Godot loads it before any addon (gui/theme/custom)";
        string notLeft = $"{broken}: left as text: Godot loads it before any addon (gui/theme/custom)";
        Assert.Contains(left, run.Lines);
        Assert.DoesNotContain(run.Lines, line => line == notLeft);
    }

    // The plan's check reports only errors; the kept text's warnings are reported once, when the output is written.
    [Fact]
    public void ProjectRunReportsAWayBackWarningOnce()
    {
        Write("project.godot", Project);
        string resource = Write(
            "t.xres",
            "<Theme xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\">\n"
                + "  <x:Resources>\n    <Gradient x:Key=\"Spare\" />\n  </x:Resources>\n</Theme>\n"
        );

        RunResult run = RunProject("--back");

        const string Message = "Nothing refers to this x:Resources entry; Godot would not save it, so it is dropped.";
        string warning = $"{resource}: warning GXC0105: {Message} (x:Resources \"Spare\")";
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(1, run.Lines.Count(line => line == warning));
        Assert.Contains("1 converted, 0 refused, 0 error(s), 1 warning(s)", run.Lines);
    }

    [Fact]
    public void ProjectSaysNothingOfAUidNoFileHolds()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"uid://none\"\n");
        Write("t.tres", ThemeResource);
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(run.Lines, line => line.Contains("left as text", StringComparison.Ordinal));
        Assert.True(File.Exists(Located("t.xres")));
        Assert.True(File.Exists(Located("main.xscn")));
    }

    [Fact]
    public void ProjectConvertsTheDefaultBusPathWhenTheSettingNamesAnother()
    {
        Write("project.godot", "config_version=5\n\n[audio]\nbuses/default_bus_layout=\"res://b.tres\"\n");
        string bus = Write("b.tres", BusLayout);
        Write("default_bus_layout.tres", BusLayout);
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(bus));
        Assert.False(File.Exists(Located("b.xres")));
        Assert.True(File.Exists(Located("default_bus_layout.xres")));
        Assert.False(File.Exists(Located("default_bus_layout.tres")));
        Assert.Contains($"{bus}: left as text: Godot loads it before any addon (audio/buses/default_bus_layout)", run.Lines);
    }

    [Fact]
    public void ProjectLeavesTheDefaultEnvironmentAndTranslations()
    {
        Write(
            "project.godot",
            "config_version=5\n\n[rendering]\nenvironment/defaults/default_environment=\"res://e.tres\"\n\n"
                + "[internationalization]\nlocale/translations=PackedStringArray(\"res://a.tres\", \"res://b.tres\")\n\n"
                + "[display]\nmouse_cursor/custom_image=\"res://c.tres\"\n"
        );
        string[] left =
        [
            Write("e.tres", ThemeResource),
            Write("a.tres", ThemeResource),
            Write("b.tres", ThemeResource),
            Write("c.tres", ThemeResource),
        ];
        string[] reasons =
        [
            "rendering/environment/defaults/default_environment",
            "internationalization/locale/translations",
            "internationalization/locale/translations",
            "display/mouse_cursor/custom_image",
        ];
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.All(left, file => Assert.True(File.Exists(file)));
        Assert.All(left, file => Assert.False(File.Exists(Path.ChangeExtension(file, ".xres"))));
        for (int index = 0; index < left.Length; index++)
        {
            Assert.Contains($"{left[index]}: left as text: Godot loads it before any addon ({reasons[index]})", run.Lines);
        }
    }

    [Fact]
    public void ProjectLeavesWhatALeftFileReaches()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"res://t.tres\"\n");
        string theme = Write(
            "t.tres",
            "[gd_resource type=\"Theme\" load_steps=2 format=3]\n\n"
                + "[ext_resource type=\"StyleBox\" path=\"res://s.tres\" id=\"1_s\"]\n\n"
                + "[resource]\ndefault_font_size = 12\nButton/styles/normal = ExtResource(\"1_s\")\n"
        );
        string style = Write("s.tres", "[gd_resource type=\"StyleBoxFlat\" format=3]\n\n[resource]\nbg_color = Color(1, 0, 0, 1)\n");
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(theme));
        Assert.True(File.Exists(style));
        Assert.False(File.Exists(Located("t.xres")));
        Assert.False(File.Exists(Located("s.xres")));
        Assert.Contains($"{style}: left as text: Godot loads it before any addon (through res://t.tres)", run.Lines);
        Assert.DoesNotContain(run.Lines, line => line.Contains("GXP0002", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectLeavesACycleOfReferencesOnce()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"res://t.tres\"\n");
        string theme = Write(
            "t.tres",
            "[gd_resource type=\"Theme\" load_steps=2 format=3]\n\n"
                + "[ext_resource type=\"StyleBox\" path=\"res://s.tres\" id=\"1_s\"]\n\n"
                + "[resource]\nButton/styles/normal = ExtResource(\"1_s\")\n"
        );
        string style = Write(
            "s.tres",
            "[gd_resource type=\"StyleBox\" load_steps=2 format=3]\n\n"
                + "[ext_resource type=\"Theme\" path=\"res://t.tres\" id=\"1_t\"]\n\n"
                + "[resource]\ntheme = ExtResource(\"1_t\")\n"
        );
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(theme));
        Assert.True(File.Exists(style));
        Assert.Single(run.Lines, $"{theme}: left as text: Godot loads it before any addon (gui/theme/custom)");
        Assert.Single(run.Lines, $"{style}: left as text: Godot loads it before any addon (through res://t.tres)");
    }

    [Fact]
    public void ProjectLeavesAFileItCannotParse()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"res://t.tres\"\n");
        string theme = Write("t.tres", "not a resource");
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("not a resource", File.ReadAllText(theme));
        Assert.True(File.Exists(Located("main.xscn")));
        Assert.Contains($"{theme}: left as text: Godot loads it before any addon (gui/theme/custom)", run.Lines);
        Assert.Contains($"{theme}: its references were not followed: Unexpected end of file (line 1, column 1)", run.Lines);
    }

    [Fact]
    public void ProjectFollowsAStartupFileUnderAddons()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"res://addons/kit/t.tres\"\n");
        string theme = Write(
            "addons/kit/t.tres",
            "[gd_resource type=\"Theme\" load_steps=2 format=3]\n\n"
                + "[ext_resource type=\"StyleBox\" path=\"res://ui/s.tres\" id=\"1_s\"]\n\n"
                + "[resource]\nButton/styles/normal = ExtResource(\"1_s\")\n"
        );
        string style = Write("ui/s.tres", "[gd_resource type=\"StyleBoxFlat\" format=3]\n\n[resource]\n");
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.False(File.Exists(Located("addons/kit/t.xres")));
        Assert.DoesNotContain(run.Lines, line => line.StartsWith($"{theme}: left as text", StringComparison.Ordinal));
        Assert.Contains($"{style}: left as text: Godot loads it before any addon (through res://addons/kit/t.tres)", run.Lines);
        Assert.False(File.Exists(Located("ui/s.xres")));
    }

    [Fact]
    public void ProjectFollowsAReferenceByItsUid()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"res://t.tres\"\n");
        Write(
            "t.tres",
            "[gd_resource type=\"Theme\" load_steps=2 format=3]\n\n"
                + "[ext_resource type=\"StyleBox\" uid=\"uid://s\" path=\"res://old/s.tres\" id=\"1_s\"]\n\n"
                + "[resource]\nButton/styles/normal = ExtResource(\"1_s\")\n"
        );
        string style = Write("ui/s.tres", "[gd_resource type=\"StyleBoxFlat\" format=3 uid=\"uid://s\"]\n\n[resource]\n");
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(style));
        Assert.False(File.Exists(Located("ui/s.xres")));
        Assert.Contains($"{style}: left as text: Godot loads it before any addon (through res://t.tres)", run.Lines);
    }

    [Fact]
    public void ProjectResolvesAUidBelowALeadingBlankLine()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"uid://abc\"\n");
        string theme = Write("t.tres", "\n[gd_resource type=\"Theme\" format=3 uid=\"uid://abc\"]\n\n[resource]\ndefault_font_size = 12\n");
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(theme));
        Assert.False(File.Exists(Located("t.xres")));
        Assert.Contains($"{theme}: left as text: Godot loads it before any addon (gui/theme/custom)", run.Lines);
    }

    [Fact]
    public void ProjectLeavesTheDefaultBusBesideATaggedKey()
    {
        Write("project.godot", "config_version=5\n\n[audio]\nbuses/default_bus_layout.mobile=\"res://m.tres\"\n");
        string mobile = Write("m.tres", BusLayout);
        string standard = Write("default_bus_layout.tres", BusLayout);
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(mobile));
        Assert.True(File.Exists(standard));
        Assert.False(File.Exists(Located("m.xres")));
        Assert.False(File.Exists(Located("default_bus_layout.xres")));
        Assert.Contains($"{mobile}: left as text: Godot loads it before any addon (audio/buses/default_bus_layout)", run.Lines);
        Assert.Contains($"{standard}: left as text: Godot loads it before any addon (audio/buses/default_bus_layout)", run.Lines);
    }

    [Fact]
    public void ProjectConvertsTheDefaultBusWhenTheKeyIsEmpty()
    {
        Write("project.godot", "config_version=5\n\n[audio]\nbuses/default_bus_layout=\"\"\n");
        Write("default_bus_layout.tres", BusLayout);
        Write("main.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(Located("default_bus_layout.xres")));
        Assert.DoesNotContain(run.Lines, line => line.Contains("left as text", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectWithOnlyLeftFilesConvertsNothing()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"res://t.tres\"\n");
        string theme = Write("t.tres", ThemeResource);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                $"{theme}: left as text: Godot loads it before any addon (gui/theme/custom)",
                $"{folder}: no .tscn or .tres files to convert",
                "0 converted, 0 refused, 0 error(s), 0 warning(s)",
            ],
            run.Lines
        );
    }

    [Fact]
    public void ProjectBackLeavesNothing()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"res://t.tres\"\n");
        string theme = Write("t.tres", ThemeResource);
        Write("default_bus_layout.tres", BusLayout);
        Write("main.tscn", ButtonScene);
        Assert.Equal(0, RunProject().ExitCode);

        RunResult run = RunProject("--back");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(run.Lines, line => line.Contains("left as text", StringComparison.Ordinal));
        Assert.True(File.Exists(Located("main.tscn")));
        Assert.Equal(ThemeResource, File.ReadAllText(theme));
    }

    [Fact]
    public void ProjectSeesAReferenceToALeftFileUnchanged()
    {
        Write("project.godot", "config_version=5\n\n[gui]\ntheme/custom=\"res://t.tres\"\n");
        Write("t.tres", ThemeResource);
        Write("main.tscn", Instancing("res://t.tres"));

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("res://t.tres", File.ReadAllText(Located("main.xscn")), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Lines, line => line.Contains(": warning ", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectConvertsADeepAddonsFolder()
    {
        Write("project.godot", Project);
        string scene = Write("game/addons/x.tscn", ButtonScene);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(Path.ChangeExtension(scene, ".xscn")));
        Assert.False(File.Exists(scene));
    }

    [Fact]
    public void ProjectSkipsHiddenGdignoreAndNested()
    {
        Write("project.godot", Project);
        string[] skipped = [Write(".hidden/a.tscn", ButtonScene), Write("ignored/a.tscn", ButtonScene), Write("nested/a.tscn", ButtonScene)];
        Write("ignored/.gdignore", "");
        Write("nested/project.godot", Project);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.All(skipped, scene => Assert.True(File.Exists(scene)));
        Assert.All(skipped, scene => Assert.False(File.Exists(Path.ChangeExtension(scene, ".xscn"))));
    }

    [Fact]
    public void ProjectBackConverts()
    {
        Write("project.godot", Project);
        Write("main.tscn", "[gd_scene format=3 uid=\"uid://bmain\"]\n\n[node name=\"Root\" type=\"Button\"]\n");
        Write("themes/t.tres", "[gd_resource type=\"Theme\" format=3 uid=\"uid://btheme\"]\n\n[resource]\ndefault_font_size = 12\n");
        Assert.Equal(0, RunProject("--nobackup").ExitCode);
        string gd = Write("a.gd", "const M = preload(\"res://main.xscn\")\nconst T = preload(\"res://themes/t.xres\")\n");
        string project = Write("project.godot", MainProject.Replace("main.tscn", "main.xscn", StringComparison.Ordinal));
        string[] sources = [Located("main.xscn"), Located("main.xscn.uid"), Located("themes/t.xres"), Located("themes/t.xres.uid")];
        Dictionary<string, byte[]> before = Snapshot(sources);

        RunResult run = RunProject("--back");

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(Located("main.tscn")));
        Assert.True(File.Exists(Located("themes/t.tres")));
        Assert.Equal("const M = preload(\"res://main.tscn\")\nconst T = preload(\"res://themes/t.tres\")\n", File.ReadAllText(gd));
        Assert.Equal(MainProject, File.ReadAllText(project));
        Assert.All(sources, source => Assert.False(File.Exists(source)));
        AssertBackedUp(Assert.Single(BackupFolders()), before);
    }

    [Fact]
    public void ProjectChangesNothingWhenASceneFails()
    {
        WriteThreeScenes(BrokenScene);
        Write("a.gd", MainScript);
        Write("project.godot", MainProject);
        TreeState before = Tree();

        RunResult run = RunProject();

        Assert.Equal(1, run.ExitCode);
        AssertNothingChanged(run, before, sourcesFound: 3);
        Assert.DoesNotContain(run.Lines, line => line.Contains("GXP0002", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Lines, line => line.Contains(" -> ", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectChangesNothingWhenAnOutputExists()
    {
        WriteThreeScenes(ThemedButton);
        Write("a.gd", MainScript);
        Write("project.godot", MainProject);
        Write("main.xscn", "old\n");
        TreeState before = Tree();

        RunResult run = RunProject();

        Assert.Equal(1, run.ExitCode);
        AssertNothingChanged(run, before, sourcesFound: 3);
    }

    [Fact]
    public void ProjectRunsAgainAfterAFix()
    {
        string[] sources = WriteThreeScenes(BrokenScene);
        Assert.Equal(1, RunProject().ExitCode);
        Write("ui/button.tscn", ThemedButton);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("3 converted, 0 refused, 0 error(s), 0 warning(s)", run.Lines);
        Assert.All(sources, source => Assert.False(File.Exists(source)));
    }

    [Fact]
    public void ProjectForceOverwritesAndBacksUpTheOldOutput()
    {
        WriteThreeScenes(ThemedButton);
        string existing = Write("main.xscn", "old\n");

        RunResult run = RunProject("--force");

        Assert.Equal(0, run.ExitCode);
        AssertInstancesTheConvertedButton(File.ReadAllText(existing));
        Assert.Equal("old\n", File.ReadAllText(Path.Combine(Assert.Single(BackupFolders()), "main.xscn")));
    }

    [Fact]
    public void ProjectKeepsEverythingWhenTheBackupFails()
    {
        string[] files = [.. WriteThreeScenes(ThemedButton), Write("a.gd", MainScript)];
        Write(BackupFolder, "in the way\n");
        Dictionary<string, byte[]> before = Snapshot(files);

        RunResult run = RunProject();

        Assert.Equal(2, run.ExitCode);
        Assert.Contains(run.ErrorLines, line => line.EndsWith(": cannot be backed up.", StringComparison.Ordinal));
        AssertUnchanged(before);
        Assert.Empty(OutputsUnder(folder));
        Assert.False(Directory.Exists(Located(StagingFolder)));
    }

    [Fact]
    public void ProjectReportsAFailedMove()
    {
        SkipUnlessWindows();
        string[] sources = WriteThreeScenes(ThemedButton);
        string existing = Write("main.xscn", "old\n");
        Dictionary<string, byte[]> before = Snapshot([.. sources, existing]);

        RunResult run;
        using (HoldOpen(existing, FileShare.Read))
        {
            run = RunProject("--force");
        }

        AssertPartConverted(run, sources);
        AssertBackedUp(Assert.Single(BackupFolders()), before);
        Assert.Equal("old\n", File.ReadAllText(existing));
        Assert.False(File.Exists(Located("ui/button.xscn")));
        Assert.False(File.Exists(Located("themes/t.xres")));
    }

    [Fact]
    public void ProjectReportsAFailedScriptWrite()
    {
        SkipUnlessWindows();
        string[] sources = WriteThreeScenes(ThemedButton);
        string gd = Write("a.gd", MainScript);

        RunResult run;
        using (HoldOpen(gd, FileShare.Read))
        {
            run = RunProject();
        }

        AssertPartConverted(run, sources);
        Assert.Equal(MainScript, File.ReadAllText(gd));
        Assert.Equal(MainScript, File.ReadAllText(Path.Combine(Assert.Single(BackupFolders()), "a.gd")));
        Assert.All(ThreeOutputs(), output => Assert.True(File.Exists(output)));
    }

    [Fact]
    public void ProjectReportsAFailedDelete()
    {
        SkipUnlessWindows();
        string[] sources = WriteThreeScenes(ThemedButton);
        Dictionary<string, byte[]> before = Snapshot(sources);

        RunResult run;
        using (HoldOpen(sources[0], FileShare.Read))
        {
            run = RunProject();
        }

        AssertPartConverted(run, sources);
        AssertUnchanged(before);
        AssertBackedUp(Assert.Single(BackupFolders()), before);
        Assert.All(ThreeOutputs(), output => Assert.True(File.Exists(output)));
    }

    [Fact]
    public void ProjectStagingFailureCountsEverySource()
    {
        WriteThreeScenes(ThemedButton);
        Write(StagingFolder, "in the way\n");
        TreeState before = Tree();

        RunResult run = RunProject();

        Assert.Equal(2, run.ExitCode);
        AssertNothingChanged(run, before, sourcesFound: 3);
    }

    [Fact]
    public void ProjectForceRemovesAStaleOutputUid()
    {
        Write("project.godot", Project);
        Write("main.tscn", ButtonScene);
        string stale = Write("main.xscn.uid", "uid://stale\n");

        RunResult run = RunProject("--force");

        Assert.Equal(0, run.ExitCode);
        Assert.False(File.Exists(stale));
        Assert.Equal("uid://stale\n", File.ReadAllText(Path.Combine(Assert.Single(BackupFolders()), "main.xscn.uid")));
    }

    [Fact]
    public void ProjectBackForceOverwritesTscnTwins()
    {
        Write("project.godot", Project);
        string main = Write("main.tscn", ButtonScene);
        Assert.Equal(0, RunProject("--nobackup").ExitCode);
        Write("main.tscn", "twin\n");

        RunResult run = RunProject("--back", "--force");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("[gd_scene", File.ReadAllText(main), StringComparison.Ordinal);
        Assert.Equal("twin\n", File.ReadAllText(Path.Combine(Assert.Single(BackupFolders()), "main.tscn")));
    }

    [Fact]
    public void ProjectChangesNothingWhenAScriptIsReadOnly()
    {
        WriteThreeScenes(ThemedButton);
        string gd = Write("a.gd", MainScript);
        TreeState before = Tree();

        RunResult run;
        using (ReadOnly(gd))
        {
            run = RunProject();
        }

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"{gd}: read-only, so it cannot be edited", run.Lines);
        AssertNothingChanged(run, before, sourcesFound: 3);
    }

    [Fact]
    public void ProjectChangesNothingWhenASourceIsReadOnly()
    {
        string main = WriteThreeScenes(ThemedButton)[0];
        TreeState before = Tree();

        RunResult run;
        using (ReadOnly(main))
        {
            run = RunProject();
        }

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"{main}: {main} is read-only", run.Lines);
        AssertNothingChanged(run, before, sourcesFound: 3);
    }

    [Fact]
    public void ProjectChangesNothingWhenAnOverwrittenOutputIsReadOnly()
    {
        string main = WriteThreeScenes(ThemedButton)[0];
        string existing = Write("main.xscn", "old\n");
        TreeState before = Tree();

        RunResult run;
        using (ReadOnly(existing))
        {
            run = RunProject("--force");
        }

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"{main}: {existing} is read-only", run.Lines);
        AssertNothingChanged(run, before, sourcesFound: 3);
    }

    [Theory]
    [InlineData("main.xscn")]
    [InlineData("main.xscn.uid")]
    public void ProjectChangesNothingWhenAFolderStandsAtTheOutput(string output)
    {
        string main = WriteThreeScenes(ThemedButton)[0];
        string standing = Located(output);
        Directory.CreateDirectory(standing);
        TreeState before = Tree();

        RunResult run = RunProject("--force");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"{main}: {standing} is a folder", run.Lines);
        AssertNothingChanged(run, before, sourcesFound: 3);
    }

    [Fact]
    public void ProjectChangesNothingWhenTwoSourcesShareAnOutput()
    {
        EnableCaseSensitivityOnWindows();
        Write("project.godot", Project);
        string lower = Write("a.tscn", ButtonScene);
        string upper = Write("a.TSCN", ButtonScene);
        if (Directory.GetFiles(folder).Count(path => Path.GetFileName(path) is "a.tscn" or "a.TSCN") < 2)
        {
            Assert.Skip("the file system is case-insensitive, so a.tscn and a.TSCN are one file");
        }
        TreeState before = Tree();

        RunResult run = RunProject();

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"{lower}: another source also converts to {Path.ChangeExtension(lower, ".xscn")}", run.Lines);
        Assert.Contains($"{upper}: another source also converts to {Path.ChangeExtension(upper, ".xscn")}", run.Lines);
        AssertNothingChanged(run, before, sourcesFound: 2);
    }

    [Fact]
    public void ProjectDoesNotEnterALinkedFolder()
    {
        WriteThreeScenes(ThemedButton);
        string outside = Write(".outside/x.tscn", ButtonScene);
        try
        {
            Directory.CreateSymbolicLink(Located("linked"), Located(".outside"));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // Creating a symlink needs a privilege (on Windows, Developer Mode or an elevated process) this run lacks.
            Assert.Skip($"symlinks cannot be created here: {exception.Message}");
        }

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(outside));
        Assert.False(File.Exists(Path.ChangeExtension(outside, ".xscn")));
    }

    [Fact]
    public void ProjectSkipsANonUtf8Script()
    {
        string[] sources = WriteThreeScenes(ThemedButton);
        byte[] script = [.. "// caf"u8, 0xE9, .. "\nstring P = \"res://main.tscn\";\n"u8];
        string cs = WriteBytes("a.cs", script);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.Single(run.Lines, $"{cs}: warning GXP0008: not UTF-8 text; not scanned or edited");
        Assert.DoesNotContain(run.ErrorLines, line => line.StartsWith(cs, StringComparison.Ordinal));
        Assert.Equal(script, File.ReadAllBytes(cs));
        Assert.All(sources, source => Assert.False(File.Exists(source)));
    }

    [Fact]
    public void ProjectChangesNothingWhenAScriptCannotBeRead()
    {
        SkipUnlessWindows();
        WriteThreeScenes(ThemedButton);
        string gd = Write("a.gd", MainScript);
        TreeState before = Tree();

        RunResult run;
        using (HoldOpen(gd, FileShare.None))
        {
            run = RunProject();
        }

        Assert.Equal(2, run.ExitCode);
        AssertNothingChanged(run, before, sourcesFound: 3);
    }

    [Fact]
    public void ProjectSaysWhenThereIsNothingToConvert()
    {
        Write("project.godot", Project);

        RunResult forward = RunProject();
        RunResult back = RunProject("--back");

        const string Summary = "0 converted, 0 refused, 0 error(s), 0 warning(s)";
        Assert.Equal(0, forward.ExitCode);
        Assert.Equal([$"{folder}: no .tscn or .tres files to convert", Summary], forward.Lines);
        Assert.Equal(0, back.ExitCode);
        Assert.Equal([$"{folder}: no .xscn or .xres files to convert", Summary], back.Lines);
        Assert.False(Directory.Exists(Located(BackupFolder)));
    }

    [Fact]
    public void ProjectWithNothingToConvertStillReadsTheMetadata()
    {
        Write("project.godot", Project);

        RunResult run = RunProject("--metadata", Located("missing.json"));

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(run.Lines);
    }

    [Fact]
    public void ProjectChangesNothingWhenALinkedScriptsTargetIsReadOnly()
    {
        WriteThreeScenes(ThemedButton);
        string target = Write(".real/a.gd", MainScript);
        string gd = Located("a.gd");
        try
        {
            File.CreateSymbolicLink(gd, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // Creating a symlink needs a privilege (on Windows, Developer Mode or an elevated process) this run lacks.
            Assert.Skip($"symlinks cannot be created here: {exception.Message}");
        }
        TreeState before = Tree();

        RunResult run;
        using (ReadOnly(target))
        {
            run = RunProject();
        }

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"{gd}: read-only, so it cannot be edited", run.Lines);
        AssertNothingChanged(run, before, sourcesFound: 3);
    }

    [Fact]
    public void ProjectChangesNothingWhenASourceLinkIsReadOnly()
    {
        string main = WriteThreeScenes(ThemedButton)[0];
        string target = LinkInPlaceOf(main);
        TreeState before = Tree();

        RunResult run;
        using (ReadOnlyLink(main, target))
        {
            run = RunProject();
        }

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"{main}: {main} is read-only", run.Lines);
        AssertNothingChanged(run, before, sourcesFound: 3);
    }

    [Fact]
    public void ProjectConvertsASourceLinkToAReadOnlyTarget()
    {
        string main = WriteThreeScenes(ThemedButton)[0];
        string target = LinkInPlaceOf(main);

        RunResult run;
        using (ReadOnly(target))
        {
            run = RunProject();
        }
        // The backup copy keeps the target's read-only attribute, which would stop the test folder's removal.
        foreach (string copy in BackupFolders().Select(backup => Path.Combine(backup, "main.tscn")).Where(File.Exists))
        {
            new ReadOnlyFile(copy).Dispose();
        }

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(Path.ChangeExtension(main, ".xscn")));
        Assert.False(File.Exists(main));
        Assert.Null(new FileInfo(main).LinkTarget);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void ProjectChangesNothingWhenAnOutputLinkLoops()
    {
        string main = WriteThreeScenes(ThemedButton)[0];
        string output = Located("main.xscn");
        string loop = Located("loop.xscn");
        CreateFileLink(output, loop);
        CreateFileLink(loop, output);
        Write("godot-xaml.json", "{}");
        string[] folders = AllFolders();
        string[] files = AllFiles();
        Dictionary<string, byte[]> contents = Snapshot(files.Except([output, loop]));

        RunResult run = RunProject("--force");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"{main}: {output} cannot be resolved", run.Lines);
        Assert.Equal(folders, AllFolders());
        Assert.Equal(files, AllFiles());
        AssertUnchanged(contents);
        Assert.Equal(loop, new FileInfo(output).LinkTarget);
        Assert.Equal(output, new FileInfo(loop).LinkTarget);
        string expected = $"{folder}: warning GXP0007: nothing changed: 3 source(s) were not converted; the project is as it was";
        Assert.Single(run.Lines, expected);
        Assert.Contains(run.Lines, line => line.StartsWith("0 converted, 1 refused, ", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectNoBackupDeletes()
    {
        string[] sources = WriteThreeScenes(ThemedButton);
        string gd = Write("a.gd", MainScript);

        RunResult run = RunProject("--nobackup");

        Assert.Equal(0, run.ExitCode);
        Assert.All(sources, source => Assert.False(File.Exists(source)));
        Assert.Equal(MainScript.Replace("main.tscn", "main.xscn", StringComparison.Ordinal), File.ReadAllText(gd));
        Assert.False(Directory.Exists(Located(BackupFolder)));
        Assert.DoesNotContain(run.Lines, line => line.Contains("backed up", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectBacksUpEachRunApart()
    {
        WriteThreeScenes(ThemedButton);

        Assert.Equal(0, RunProject().ExitCode);
        Assert.Equal(0, RunProject("--back").ExitCode);

        Assert.Equal(2, BackupFolders().Length);
    }

    [Fact]
    public void ProjectSkipsItsBackupFolder()
    {
        WriteThreeScenes(ThemedButton);
        Assert.Equal(0, RunProject().ExitCode);
        string backup = Assert.Single(BackupFolders());
        Dictionary<string, byte[]> backedUp = Snapshot(Directory.GetFiles(backup, "*", SearchOption.AllDirectories));

        RunResult run = RunProject("--back");

        Assert.Equal(0, run.ExitCode);
        AssertUnchanged(backedUp);
        Assert.Empty(OutputsUnder(backup));
        Assert.DoesNotContain(
            run.Lines,
            line => line.Contains(BackupFolder, StringComparison.Ordinal) && !line.Contains("backed up", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ProjectPrintsNoSharedUidWhenRemoved()
    {
        Write("project.godot", Project);
        Write("main.tscn", "[gd_scene format=3 uid=\"uid://bmain\"]\n\n[node name=\"Root\" type=\"Button\"]\n");

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(run.Lines, line => line.Contains("GXP0003", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectBackupIsIgnoredByGit()
    {
        WriteThreeScenes(ThemedButton);

        Assert.Equal(0, RunProject().ExitCode);

        Assert.Equal("*\n", File.ReadAllText(Path.Combine(Located(BackupFolder), ".gitignore")));
    }

    [Fact]
    public void ProjectKeepsAnExistingBackupGitignore()
    {
        WriteThreeScenes(ThemedButton);
        string gitignore = Write(Path.Combine(BackupFolder, ".gitignore"), "*.tscn\n");

        Assert.Equal(0, RunProject().ExitCode);

        Assert.Equal("*.tscn\n", File.ReadAllText(gitignore));
    }

    [Fact]
    public void ProjectLeavesNoStagingFolder()
    {
        WriteThreeScenes(ThemedButton);

        RunResult run = RunProject();

        Assert.Equal(0, run.ExitCode);
        Assert.False(Directory.Exists(Located(StagingFolder)));
    }

    [Fact]
    public void ConvertNeedsAPathOrProject() => AssertUsageError(["convert"], "convert needs a path or --project.");

    [Fact]
    public void ProjectTakesNoPaths()
    {
        Write("project.godot", Project);
        string main = Write("main.tscn", ButtonScene);

        AssertUsageError(["convert", "--project", folder, main], "--project takes no paths.");
    }

    [Fact]
    public void BackNeedsProject() => AssertUsageError(["convert", "--back", Write("main.tscn", ButtonScene)], "--back needs --project.");

    [Fact]
    public void NoBackupNeedsProject() => AssertUsageError(["convert", "--nobackup", Write("main.tscn", ButtonScene)], "--nobackup needs --project.");

    [Fact]
    public void ProjectNeedsAProjectGodot()
    {
        Write("main.tscn", ButtonScene);

        AssertUsageError(["convert", "--project", folder], $"{folder}: no project.godot in this folder.");
    }

    [Fact]
    public void PlainConvertStillWritesNoScript()
    {
        string[] sources = WriteThreeScenes(ThemedButton);
        string gd = Write("a.gd", MainScript);

        RunResult run = RunConvert(folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(MainScript, File.ReadAllText(gd));
        Assert.All(sources, source => Assert.True(File.Exists(source)));
        Assert.False(Directory.Exists(Located(BackupFolder)));
    }

    // main instances ui/button, and ui/button, unless it is replaced, uses themes/t.
    private string[] WriteThreeScenes(string button)
    {
        if (!File.Exists(Located("project.godot")))
        {
            Write("project.godot", Project);
        }
        return [Write("main.tscn", Instancing("res://ui/button.tscn")), Write("ui/button.tscn", button), Write("themes/t.tres", ThemeResource)];
    }

    private string[] ThreeOutputs() => [Located("main.xscn"), Located("ui/button.xscn"), Located("themes/t.xres")];

    private RunResult RunProject(params string[] args)
    {
        Write("godot-xaml.json", "{}");
        return RunIn(folder, ["convert", "--project", folder, .. args]);
    }

    private void AssertUsageError(string[] args, string message)
    {
        RunResult run = RunIn(folder, args);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal([message], run.ErrorLines);
        Assert.Empty(OutputsUnder(folder));
        Assert.False(Directory.Exists(Located(BackupFolder)));
    }

    // Every folder and every file's bytes under the test's folder, with no exemption.
    private void AssertNothingChanged(RunResult run, TreeState before, int sourcesFound)
    {
        TreeState after = Tree();
        Assert.Equal(before.Folders, after.Folders);
        Assert.Equal(before.Files.Keys.Order(StringComparer.Ordinal), after.Files.Keys.Order(StringComparer.Ordinal));
        AssertUnchanged(before.Files);
        string expected = $"{folder}: warning GXP0007: nothing changed: {sourcesFound} source(s) were not converted; the project is as it was";
        Assert.Single(run.Lines, expected);
    }

    private void AssertPartConverted(RunResult run, string[] originals)
    {
        Assert.Equal(2, run.ExitCode);
        Assert.Contains($"{folder}: the project is part converted; restore it from {Assert.Single(BackupFolders())}", run.ErrorLines);
        Assert.All(originals, original => Assert.True(File.Exists(original)));
        Assert.False(Directory.Exists(Located(StagingFolder)));
    }

    // The tree as the run will find it; the config file RunProject writes is written first, so it is in the snapshot.
    private TreeState Tree()
    {
        Write("godot-xaml.json", "{}");
        string[] folders = [.. Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        return new TreeState(folders, Snapshot(Directory.GetFiles(folder, "*", SearchOption.AllDirectories)));
    }

    // On Windows the test's still-empty folder is made case-sensitive, so a.tscn and a.TSCN can both exist in it.
    private void EnableCaseSensitivityOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var start = new ProcessStartInfo("fsutil.exe", ["file", "setCaseSensitiveInfo", folder, "enable"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using Process fsutil = Process.Start(start)!;
        string said = fsutil.StandardOutput.ReadToEnd() + fsutil.StandardError.ReadToEnd();
        fsutil.WaitForExit();
        if (fsutil.ExitCode != 0)
        {
            Assert.Skip($"fsutil could not make the folder case-sensitive (exit {fsutil.ExitCode}): {said.Trim()}");
        }
    }

    private static void SkipUnlessWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("only Windows keeps another process from replacing or deleting a file held open");
        }
    }

    private static FileStream HoldOpen(string path, FileShare share) => new(path, FileMode.Open, FileAccess.Read, share);

    private static ReadOnlyFile ReadOnly(string path)
    {
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        return new ReadOnlyFile(path);
    }

    private sealed record TreeState(string[] Folders, Dictionary<string, byte[]> Files);

    // Clears the attribute again, since a read-only file stops the test folder's removal on Windows.
    private sealed class ReadOnlyFile(string path) : IDisposable
    {
        public void Dispose() => File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
    }

    // Moves a file into the hidden .real folder, which no scan enters, and leaves a file link to it in its place.
    private string LinkInPlaceOf(string path)
    {
        string target = Located(Path.Combine(".real", Path.GetFileName(path)));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(path, target);
        CreateFileLink(path, target);
        return target;
    }

    private static void CreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // Creating a symlink needs a privilege (on Windows, Developer Mode or an elevated process) this run lacks.
            Assert.Skip($"symlinks cannot be created here: {exception.Message}");
        }
    }

    // Sets a link's own read-only attribute and leaves its target writable. Where File.SetAttributes follows the link
    // to its target, attrib /L sets the link's own attribute instead; both are read back to tell which happened.
    private static ReadOnlyLinkEntry ReadOnlyLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("only Windows gives a link a read-only attribute of its own");
        }
        File.SetAttributes(link, File.GetAttributes(link) | FileAttributes.ReadOnly);
        bool followed = File.GetAttributes(target).HasFlag(FileAttributes.ReadOnly);
        if (followed)
        {
            File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);
            Attrib("+R", link);
        }
        Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReadOnly), $"{link} is not read-only");
        Assert.False(File.GetAttributes(target).HasFlag(FileAttributes.ReadOnly), $"{target} is read-only");
        return new ReadOnlyLinkEntry(link, followed);
    }

    private static void Attrib(string change, string link)
    {
        var start = new ProcessStartInfo("attrib.exe", [change, "/L", link])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using Process attrib = Process.Start(start)!;
        string said = attrib.StandardOutput.ReadToEnd() + attrib.StandardError.ReadToEnd();
        attrib.WaitForExit();
        Assert.True(attrib.ExitCode == 0, $"attrib {change} /L {link} failed (exit {attrib.ExitCode}): {said.Trim()}");
    }

    // Clears the link's own attribute the way it was set, since a read-only link stops the test folder's removal.
    private sealed class ReadOnlyLinkEntry(string link, bool byAttrib) : IDisposable
    {
        public void Dispose()
        {
            if (byAttrib)
            {
                Attrib("-R", link);
                return;
            }
            File.SetAttributes(link, File.GetAttributes(link) & ~FileAttributes.ReadOnly);
        }
    }

    private string[] AllFolders() => [.. Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    private string[] AllFiles() => [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    // An instance with no uid is written as an element in the namespace of the output's folder.
    private static void AssertInstancesTheConvertedButton(string main)
    {
        Assert.Contains("<ui:button", main, StringComparison.Ordinal);
        Assert.DoesNotContain("res://ui/button.tscn", main, StringComparison.Ordinal);
    }

    private static void AssertUnchanged(Dictionary<string, byte[]> before)
    {
        foreach ((string file, byte[] bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(file));
        }
    }

    private void AssertBackedUp(string backup, Dictionary<string, byte[]> before)
    {
        foreach ((string file, byte[] bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(backup, Path.GetRelativePath(folder, file))));
        }
    }

    private static Dictionary<string, byte[]> Snapshot(IEnumerable<string> files) => files.ToDictionary(file => file, File.ReadAllBytes);

    private string[] BackupFolders()
    {
        string backups = Located(BackupFolder);
        return Directory.Exists(backups) ? Directory.GetDirectories(backups) : [];
    }

    private static string[] OutputsUnder(string root) =>
        [
            .. Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path) is ".xscn" or ".xres" || path.EndsWith(".uid", StringComparison.Ordinal)),
        ];

    private string Located(string relativePath) => Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
