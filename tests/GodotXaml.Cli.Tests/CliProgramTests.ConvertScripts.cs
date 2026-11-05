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

    [Fact]
    public void ConvertRollsUpBuiltPathsPerFile()
    {
        string file = WriteConvertedButtonAnd("a.gd", FiveFragments);

        RunResult run = RunConvert(folder);

        string[] expected =
        [
            $"{file}(1,16): warning GXP0006: {Fragment}",
            $"{file}(2,16): warning GXP0006: {Fragment}",
            $"{file}(3,16): warning GXP0006: {Fragment}",
            $"{file}(4,16): warning GXP0006: and 2 more like it in this file; none is rewritten",
        ];
        Assert.Equal(expected, ScriptWarnings(run));
        Assert.Contains("1 converted, 0 refused, 0 error(s), 5 warning(s)", run.Lines);
    }

    [Fact]
    public void TheSummaryCountsEveryHitTheRollUpStandsFor()
    {
        const string SevenFragments =
            "var aa = \"x\" + \".tscn\"\nvar bb = \"x\" + \".tscn\"\nvar cc = \"x\" + \".tscn\"\n"
            + "var dd = \"x\" + \".tscn\"\nvar ee = \"x\" + \".tscn\"\nvar ff = \"x\" + \".tscn\"\nvar gg = \"x\" + \".tscn\"\n";
        string file = WriteConvertedButtonAnd("a.gd", SevenFragments);

        RunResult run = RunConvert(folder);

        string[] expected =
        [
            $"{file}(1,16): warning GXP0006: {Fragment}",
            $"{file}(2,16): warning GXP0006: {Fragment}",
            $"{file}(3,16): warning GXP0006: {Fragment}",
            $"{file}(4,16): warning GXP0006: and 4 more like it in this file; none is rewritten",
        ];
        Assert.Equal(expected, ScriptWarnings(run));
        Assert.Contains("1 converted, 0 refused, 0 error(s), 7 warning(s)", run.Lines);
    }

    [Fact]
    public void ConvertPrintsAllOfThreeBuiltPaths()
    {
        string file = WriteConvertedButtonAnd("a.gd", ThreeFragments);

        RunResult run = RunConvert(folder);

        string[] expected =
        [
            $"{file}(1,16): warning GXP0006: {Fragment}",
            $"{file}(2,16): warning GXP0006: {Fragment}",
            $"{file}(3,16): warning GXP0006: {Fragment}",
        ];
        Assert.Equal(expected, ScriptWarnings(run));
    }

    [Fact]
    public void ConvertKeepsWholeReferencesOutOfTheRollup()
    {
        string file = WriteConvertedButtonAnd("a.gd", FiveFragments + "var w1 = \"res://ui/button.tscn\"\nvar w2 = \"res://ui/button.tscn\"\n");

        RunResult run = RunConvert(folder);

        string[] expected =
        [
            $"{file}(1,16): warning GXP0006: {Fragment}",
            $"{file}(2,16): warning GXP0006: {Fragment}",
            $"{file}(3,16): warning GXP0006: {Fragment}",
            $"{file}(4,16): warning GXP0006: and 2 more like it in this file; none is rewritten",
            $"{file}(6,10): {NoUidWarning}",
            $"{file}(7,10): {NoUidWarning}",
        ];
        Assert.Equal(expected, ScriptWarnings(run));
    }

    private const string Fragment = "ends in .tscn, and this run converts such files to .xscn; a path built from it is not rewritten";

    private const string ThreeFragments = "var aa = \"x\" + \".tscn\"\nvar bb = \"x\" + \".tscn\"\nvar cc = \"x\" + \".tscn\"\n";

    private const string FiveFragments =
        "var aa = \"x\" + \".tscn\"\nvar bb = \"x\" + \".tscn\"\nvar cc = \"x\" + \".tscn\"\n"
        + "var dd = \"x\" + \".tscn\"\nvar ee = \"x\" + \".tscn\"\n";

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
