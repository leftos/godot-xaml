namespace GodotXaml.Cli.Tests;

/// <summary><see cref="ScriptPaths"/> finding and rewriting the string literals of scripts and <c>project.godot</c> by their value.</summary>
public sealed class ScriptPathsTests
{
    private const string Key = "res://ui/button.tscn";
    private const string Target = "res://ui/button.xscn";

    private static readonly Dictionary<string, string> Renames = new(StringComparer.Ordinal) { [Key] = Target };

    [Theory]
    [InlineData("preload(\"res://ui/button.tscn\")", "Whole Regular 1")]
    [InlineData("const X = 'res://ui/button.tscn'", "Whole Regular 1")]
    [InlineData("load(&\"res://ui/button.tscn\")", "Whole Regular 1")]
    [InlineData("load(r\"res://ui/button.tscn\")", "Whole Raw 1")]
    [InlineData("\"\"\"res://ui/button.tscn\"\"\"", "Whole Regular 3")]
    [InlineData("my_loader(\"res://ui/button.tscn\")", "Whole Regular 1")]
    [InlineData("# load(\"res://ui/button.tscn\")", "")]
    [InlineData("x = \"#\" + \"res://ui/button.tscn\"", "Whole Regular 1", Key)]
    [InlineData("\"res://ui/button.tscn.import\"", "Inside Regular 1")]
    [InlineData("\"could not load res://ui/button.tscn\"", "Inside Regular 1")]
    [InlineData("\"res://ui/\" + name + \".tscn\"", "Fragment Regular 1", ".tscn")]
    [InlineData("\"*.tscn\"", "Fragment Regular 1")]
    [InlineData("\"res://ui/other.tscn\"", "")]
    [InlineData("\"uid://abc\"", "")]
    [InlineData("r\"a\\\"b res://ui/button.tscn\"", "Inside Raw 1")]
    [InlineData("\"res:\\u002f\\u002fui/button.tscn\"", "Whole Regular 1", Key)]
    [InlineData("\"res://ui/\\\nbutton.tscn\"", "Whole Regular 1", Key)]
    [InlineData("\"res://ui/\\\r\nbutton.tscn\"", "")]
    public void GdScriptLiterals(string text, string expected, string? on = null) => AssertHits(text, ScriptKind.GdScript, expected, on);

    [Theory]
    [InlineData("GD.Load<PackedScene>(\"res://ui/button.tscn\")", "Whole Regular 1")]
    [InlineData("const string P = @\"res://ui/button.tscn\";", "Whole Verbatim 1")]
    [InlineData("\"\"\"res://ui/button.tscn\"\"\"", "Whole Raw 3")]
    [InlineData("// \"res://ui/button.tscn\"", "")]
    [InlineData("/// <see href=\"res://ui/button.tscn\"/>", "")]
    [InlineData("/* \"res://ui/button.tscn\" */", "")]
    [InlineData("#region it's \"res://ui/button.tscn\"\nx = 'a';", "")]
    [InlineData("char c = '\"'; string s = \"res://ui/button.tscn\";", "Whole Regular 1")]
    [InlineData("char c = '\\''; string s = \"res://ui/button.tscn\";", "Whole Regular 1")]
    [InlineData("$\"res://ui/{n}.tscn\"", "Fragment Interpolated 1")]
    [InlineData("$\"res://ui/button.tscn\"", "Whole Interpolated 1")]
    [InlineData("$\"{Load(\"res://ui/button.tscn\")} x\"", "Whole Regular 1", Key)]
    [InlineData("$\"res://ui/button.tscn?{q}\"", "Inside Interpolated 1")]
    [InlineData("\"res://\" + \"ui/button.tscn\"", "Fragment Regular 1", "ui/button.tscn")]
    [InlineData("@\"a\"\"res://ui/button.tscn\"", "Inside Verbatim 1")]
    [InlineData("$$\"\"\"{{n}}.tscn\"\"\"", "Fragment Interpolated 3")]
    [InlineData("char c = '\\''; char d = '\"'; string s = \"res://ui/button.tscn\";", "Whole Regular 1", Key)]
    [InlineData("\f#region it's \"res://ui/button.tscn\"", "")]
    [InlineData("// a\u2028var q = \"res://ui/button.tscn\";", "Whole Regular 1", Key)]
    public void CSharpLiterals(string text, string expected, string? on = null) => AssertHits(text, ScriptKind.CSharp, expected, on);

    [Theory]
    [InlineData("void F() {\n var a = $\"{d:dd//MM}\";\n}\nvar q = \"res://ui/button.tscn\";\n", "Whole Regular 1", Key)]
    [InlineData("var a = $\"{d:/*}\";\nvar q = \"res://ui/button.tscn\";\n", "Whole Regular 1", Key)]
    [InlineData("Log($\"{h:0.0\\\"}\");\nvar q = \"res://ui/button.tscn\";\n", "Whole Regular 1", Key)]
    [InlineData("Log($@\"{x:it's}\", 'a');\nvar q = \"res://ui/button.tscn\";\n", "Whole Regular 1", Key)]
    [InlineData("Log($@\"{x:a\"\"b}\");\nvar q = \"res://ui/button.tscn\";\n", "Whole Regular 1", Key)]
    [InlineData("$\"\"\"{d:dd//MM}\"\"\"\nvar q = \"res://ui/button.tscn\";\n", "Whole Regular 1", Key)]
    [InlineData("$$\"\"\"{{d:dd//MM}}\"\"\"\nvar q = \"res://ui/button.tscn\";\n", "Whole Regular 1", Key)]
    [InlineData("void F() {\n var a = $\"{d:dd//MM}\";\n}\n// \"a \"res://ui/button.tscn\" b\nvoid G() { }\n", "")]
    [InlineData("$\"{d:#,##0.00}\"; var q = \"res://ui/button.tscn\";", "Whole Regular 1", Key)]
    [InlineData("$\"{d:HH':'mm}\"; var q = \"res://ui/button.tscn\";", "Whole Regular 1", Key)]
    [InlineData("$\"{x,10:F2}\"; var q = \"res://ui/button.tscn\";", "Whole Regular 1", Key)]
    [InlineData("$\"{(global::Foo.Get(\"res://ui/button.tscn\"))}\"", "Whole Regular 1", Key)]
    [InlineData("void F() {\n var a = $\"{d::dd//MM}\";\n}\nvar q = \"res://ui/button.tscn\";\n", "Whole Regular 1", Key)]
    [InlineData("$\"{(a ? \"res://ui/button.tscn\" : \"b\")}\"", "Whole Regular 1", Key)]
    [InlineData("$\"{F(x ? 1 : 2):N0}\"; var q = \"res://ui/button.tscn\";", "Whole Regular 1", Key)]
    [InlineData("$\"{arr[a ? 1 : 2]:x}\"; var q = \"res://ui/button.tscn\";", "Whole Regular 1", Key)]
    public void CSharpFormatClauses(string text, string expected, string? on = null) => AssertHits(text, ScriptKind.CSharp, expected, on);

    [Theory]
    [InlineData("run/main_scene=\"res://ui/button.tscn\"", "Whole Regular 1")]
    [InlineData("Button=\"*res://ui/button.tscn\"", "Whole Regular 1")]
    [InlineData("Button=\"res://ui/button.tscn\"", "Whole Regular 1")]
    [InlineData("run/main_scene=\"uid://abc\"", "")]
    [InlineData("; run/main_scene=\"res://ui/button.tscn\"", "")]
    [InlineData("x=\"*.tscn\"", "")]
    [InlineData("x=\"a res://ui/button.tscn\"", "Inside Regular 1")]
    [InlineData("x=PackedStringArray(\"res://ui/button.tscn\", \"b\")", "Whole Regular 1")]
    public void ProjectFileLiterals(string text, string expected, string? on = null) => AssertHits(text, ScriptKind.ProjectFile, expected, on);

    [Fact]
    public void RewriteKeepsEverythingButThePath()
    {
        const string Text = "# see \"res://ui/button.tscn\"\r\nvar a = \"res://ui/button.tscn\"\r\n\tvar b = load('res://ui/button.tscn')\r\n";
        const string Expected = "# see \"res://ui/button.tscn\"\r\nvar a = \"res://ui/button.xscn\"\r\n\tvar b = load('res://ui/button.xscn')\r\n";

        ScriptRewrite rewrite = ScriptPaths.Rewrite(Text, ScriptKind.GdScript, Renames);

        Assert.Equal(Expected, rewrite.Text);
        Assert.Equal(2, rewrite.Rewritten);
        Assert.Empty(rewrite.Left);
    }

    [Fact]
    public void RewriteKeepsTheStarInProjectGodot() =>
        Assert.Equal("A=\"*res://ui/button.xscn\"", ScriptPaths.Rewrite("A=\"*res://ui/button.tscn\"", ScriptKind.ProjectFile, Renames).Text);

    [Fact]
    public void RewriteKeepsAPrefixAndQuotes() =>
        Assert.Equal("&'res://ui/button.xscn'", ScriptPaths.Rewrite("&'res://ui/button.tscn'", ScriptKind.GdScript, Renames).Text);

    [Fact]
    public void RewriteDecodedLiteralWritesTheValuePlain() =>
        Assert.Equal("\"res://ui/button.xscn\"", ScriptPaths.Rewrite("\"res:\\u002f\\u002fui/button.tscn\"", ScriptKind.GdScript, Renames).Text);

    [Fact]
    public void RewriteLeavesAnInsideHit()
    {
        const string Text = "\"could not load res://ui/button.tscn\"";

        ScriptRewrite rewrite = ScriptPaths.Rewrite(Text, ScriptKind.GdScript, Renames);

        Assert.Equal(Text, rewrite.Text);
        Assert.Equal(0, rewrite.Rewritten);
        Assert.Equal(ScriptHitKind.Inside, Assert.Single(rewrite.Left).Kind);
    }

    [Fact]
    public void RewriteLeavesARawLiteralWhoseValueCannotBeWritten()
    {
        const string Text = "r\"res://a.tscn\"";
        Dictionary<string, string> renames = new(StringComparer.Ordinal) { ["res://a.tscn"] = "res://a\"b.xscn" };

        ScriptRewrite rewrite = ScriptPaths.Rewrite(Text, ScriptKind.GdScript, renames);

        Assert.Equal(Text, rewrite.Text);
        Assert.Equal(0, rewrite.Rewritten);
        Assert.Equal(ScriptHitKind.Whole, Assert.Single(rewrite.Left).Kind);
    }

    [Fact]
    public void RewriteDoublesBracesInAnInterpolatedLiteral()
    {
        Dictionary<string, string> renames = new(StringComparer.Ordinal) { ["res://x.tscn"] = "res://{a}.xscn" };

        Assert.Equal("$\"res://{{a}}.xscn\"", ScriptPaths.Rewrite("$\"res://x.tscn\"", ScriptKind.CSharp, renames).Text);
    }

    [Fact]
    public void RewriteInsideAHole() =>
        Assert.Equal(
            "$\"{Load(\"res://ui/button.xscn\")}\"",
            ScriptPaths.Rewrite("$\"{Load(\"res://ui/button.tscn\")}\"", ScriptKind.CSharp, Renames).Text
        );

    [Theory]
    [InlineData("$@\"res://x.tscn\"", "$@\"res://a\"\"{{b}}\\c.xscn\"")]
    [InlineData("@$\"res://x.tscn\"", "@$\"res://a\"\"{{b}}\\c.xscn\"")]
    public void RewriteSpellsAnInterpolatedVerbatimLiteral(string text, string expected) =>
        Assert.Equal(expected, ScriptPaths.Rewrite(text, ScriptKind.CSharp, Renaming("res://a\"{b}\\c.xscn")).Text);

    [Fact]
    public void RewriteSpellsARawInterpolatedLiteral() =>
        Assert.Equal(
            "$\"\"\"res://a\\b.xscn\"\"\"",
            ScriptPaths.Rewrite("$\"\"\"res://x.tscn\"\"\"", ScriptKind.CSharp, Renaming("res://a\\b.xscn")).Text
        );

    [Fact]
    public void RewriteLeavesARawInterpolatedLiteralWhoseValueCannotBeWritten()
    {
        const string Text = "$\"\"\"res://x.tscn\"\"\"";

        ScriptRewrite rewrite = ScriptPaths.Rewrite(Text, ScriptKind.CSharp, Renaming("res://a\"b.xscn"));

        Assert.Equal(Text, rewrite.Text);
        Assert.Equal(ScriptHitKind.Whole, Assert.Single(rewrite.Left).Kind);
    }

    [Fact]
    public void RewriteDoublesAQuoteInAVerbatimLiteral() =>
        Assert.Equal("@\"res://a\"\"b.xscn\"", ScriptPaths.Rewrite("@\"res://x.tscn\"", ScriptKind.CSharp, Renaming("res://a\"b.xscn")).Text);

    [Theory]
    [InlineData("\"res://x.tscn\"", "\"res://a\\\\b\\\"'.xscn\"")]
    [InlineData("'res://x.tscn'", "'res://a\\\\b\"\\'.xscn'")]
    public void RewriteEscapesARegularLiteral(string text, string expected) =>
        Assert.Equal(expected, ScriptPaths.Rewrite(text, ScriptKind.GdScript, Renaming("res://a\\b\"'.xscn")).Text);

    [Fact]
    public void RewriteLeavesACSharpRawLiteralWhoseValueCannotBeWritten()
    {
        const string Text = "\"\"\"res://x.tscn\"\"\"";

        ScriptRewrite rewrite = ScriptPaths.Rewrite(Text, ScriptKind.CSharp, Renaming("res://a\"b.xscn"));

        Assert.Equal(Text, rewrite.Text);
        Assert.Equal(ScriptHitKind.Whole, Assert.Single(rewrite.Left).Kind);
    }

    [Fact]
    public void FindGivesAStarredHitAKeyWithoutTheStar() =>
        Assert.Equal(Key, Assert.Single(ScriptPaths.Find("A=\"*res://ui/button.tscn\"", ScriptKind.ProjectFile, Renames)).Key);

    [Fact]
    public void FindGivesInsideHitsInOrdinalKeyOrder()
    {
        Dictionary<string, string> renames = new(StringComparer.Ordinal) { ["res://b.tscn"] = "res://b.xscn", ["res://a.tscn"] = "res://a.xscn" };

        IReadOnlyList<ScriptHit> hits = ScriptPaths.Find("\"res://b.tscn and res://a.tscn\"", ScriptKind.GdScript, renames);

        Assert.Equal(["res://a.tscn", "res://b.tscn"], hits.Select(hit => hit.Key));
    }

    [Fact]
    public void FindTakesTheFirstKeyForAFragment()
    {
        Dictionary<string, string> renames = new(StringComparer.Ordinal) { ["res://b.tscn"] = "res://b.xscn", ["res://a.tscn"] = "res://a.other" };

        ScriptHit hit = Assert.Single(ScriptPaths.Find("\"*.tscn\"", ScriptKind.GdScript, renames));

        Assert.Equal((".tscn", ".other"), (hit.Key, hit.Value));
    }

    [Theory]
    [InlineData("a = \"res://ui/button.tscn\"\nb = \"open", nameof(ScriptKind.GdScript))]
    [InlineData("a = \"res://ui/button.tscn\"; b = \"open", nameof(ScriptKind.CSharp))]
    [InlineData("a=\"res://ui/button.tscn\"\nb=\"open", nameof(ScriptKind.ProjectFile))]
    public void UnterminatedStringEndsTheFileQuietly(string text, string kind) =>
        Assert.Equal(Key, Assert.Single(ScriptPaths.Literals(text, Enum.Parse<ScriptKind>(kind))).Text);

    [Theory]
    [InlineData(64, 1)]
    [InlineData(65, 0)]
    public void DeepInterpolationEndsTheFileQuietly(int levels, int laterHits)
    {
        string holes = string.Concat(Enumerable.Repeat("$\"{", levels)) + "x" + string.Concat(Enumerable.Repeat("}\"", levels));

        Assert.Equal(laterHits, ScriptPaths.Find(holes + "; var q = \"res://ui/button.tscn\";", ScriptKind.CSharp, Renames).Count);
    }

    private static Dictionary<string, string> Renaming(string value) => new(StringComparer.Ordinal) { ["res://x.tscn"] = value };

    private static void AssertHits(string text, ScriptKind kind, string expected, string? on)
    {
        IReadOnlyList<ScriptHit> hits = ScriptPaths.Find(text, kind, Renames);

        Assert.Equal(expected, string.Join("; ", hits.Select(hit => $"{hit.Kind} {hit.Literal.Form} {hit.Literal.QuoteCount}")));
        if (on is not null)
        {
            Assert.Equal(on, Assert.Single(hits).Literal.Text);
        }
    }
}
