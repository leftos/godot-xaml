namespace GodotXaml.Cli.Tests;

/// <summary>The <c>convert</c> command warning about scripts and <c>project.godot</c>, which a plain run never writes.</summary>
public sealed partial class CliProgramTests
{
    private const string NoUidWarning =
        "warning GXP0002: refers to res://ui/button.tscn, which this run converts to res://ui/button.xscn; "
        + "no uid on this reference; it breaks once the source is removed";

    [Fact]
    public void ConvertWarnsAboutAScriptLiteral()
    {
        const string Script = "extends Node\nconst B = preload(\"res://ui/button.tscn\")\n";
        string file = WriteConvertedButtonAnd("a.gd", Script);

        RunResult run = RunConvert(folder);

        Assert.Equal($"{file}(2,19): {NoUidWarning}", Assert.Single(ScriptWarnings(run)));
        Assert.Equal(Script, File.ReadAllText(file));
    }

    [Fact]
    public void ConvertWarnsAboutACSharpLiteral()
    {
        const string Script = "class A\n{\n    string P = \"res://ui/button.tscn\";\n}\n";
        string file = WriteConvertedButtonAnd("a.cs", Script);

        RunResult run = RunConvert(folder);

        Assert.Equal($"{file}(3,16): {NoUidWarning}", Assert.Single(ScriptWarnings(run)));
        Assert.Equal(Script, File.ReadAllText(file));
    }

    [Fact]
    public void ConvertWarnsAboutProjectGodotsMainScene()
    {
        const string Godot = "config_version=5\n\n[application]\nrun/main_scene=\"res://ui/button.tscn\"\n";
        Write("ui/button.tscn", ButtonScene);
        string project = Write("project.godot", Godot);

        RunResult run = RunConvert(folder);

        Assert.Equal($"{project}(4,16): {NoUidWarning}", Assert.Single(ScriptWarnings(run)));
        Assert.Equal(Godot, File.ReadAllText(project));
    }

    [Fact]
    public void ConvertWarnsAboutABuiltPath()
    {
        string file = WriteConvertedButtonAnd("a.gd", "var p = \"res://ui/\" + name + \".tscn\"\n");

        RunResult run = RunConvert(folder);

        Assert.Equal(
            $"{file}(1,30): warning GXP0006: ends in .tscn, and this run converts such files to .xscn; a path built from it is not rewritten",
            Assert.Single(ScriptWarnings(run))
        );
    }

    [Fact]
    public void ConvertWarnsAboutAPathInsideAString()
    {
        string file = WriteConvertedButtonAnd("a.gd", "print(\"could not load res://ui/button.tscn\")\n");

        RunResult run = RunConvert(folder);

        Assert.Equal(
            $"{file}(1,7): warning GXP0006: holds res://ui/button.tscn, which this run converts to res://ui/button.xscn, "
                + "inside a longer string; it is not rewritten",
            Assert.Single(ScriptWarnings(run))
        );
    }

    [Fact]
    public void ConvertSkipsACommentInAScript()
    {
        WriteConvertedButtonAnd("a.gd", "# preload(\"res://ui/button.tscn\")\n");

        RunResult run = RunConvert(folder);

        Assert.Empty(ScriptWarnings(run));
    }

    [Fact]
    public void ConvertSkipsBinAndObjBesideACsproj()
    {
        const string Script = "string P = \"res://ui/button.tscn\";\n";
        WriteConvertedButtonAnd("app/obj/skipped.cs", Script);
        Write("app/bin/skipped.cs", Script);
        Write("app/a.csproj", "<Project />\n");
        string kept = Write("lib/obj/kept.cs", Script);

        RunResult run = RunConvert(folder);

        Assert.Equal($"{kept}(1,12): {NoUidWarning}", Assert.Single(ScriptWarnings(run)));
    }

    [Fact]
    public void ConvertSkipsANestedProjectsScripts()
    {
        WriteConvertedButtonAnd("nested/a.gd", "var p = \"res://ui/button.tscn\"\n");
        Write("nested/project.godot", Project);

        RunResult run = RunConvert(folder);

        Assert.Empty(ScriptWarnings(run));
    }

    [Fact]
    public void ConvertStillExitsZeroWithScriptWarnings()
    {
        WriteConvertedButtonAnd("a.gd", "var p = \"res://ui/button.tscn\"\nvar q = \"*.tscn\"\n");

        RunResult run = RunConvert(folder);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(2, ScriptWarnings(run).Length);
    }

    private string WriteConvertedButtonAnd(string script, string text)
    {
        Write("project.godot", Project);
        Write("ui/button.tscn", ButtonScene);
        return Write(script, text);
    }

    private static string[] ScriptWarnings(RunResult run) =>
        [
            .. run.Lines.Where(line =>
                line.Contains("warning GXP0002", StringComparison.Ordinal) || line.Contains("warning GXP0006", StringComparison.Ordinal)
            ),
        ];
}
