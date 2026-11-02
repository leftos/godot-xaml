namespace GodotXaml.Cli.Tests;

/// <summary><see cref="ReferenceScan.Quoted"/> finding whole quoted strings by each file kind's own quoting rules.</summary>
public sealed class ReferenceScanTests
{
    private const string Button = "res://ui/button.tscn";
    private const string Theme = "res://themes/t.tres";

    [Fact]
    public void GodotTextFindsAnExtResourcePath() =>
        AssertFound("[ext_resource type=\"PackedScene\" path=\"res://ui/button.tscn\" id=\"1\"]", QuoteGrammar.GodotText, Button);

    [Fact]
    public void GodotTextFindsAPathAfterAnEscapedQuote() =>
        AssertFound("paths = PackedStringArray(\"5\\\" screen\", \"res://ui/button.tscn\")", QuoteGrammar.GodotText, Button);

    [Fact]
    public void GodotTextFindsAPathAfterAnEscapedBackslash() =>
        AssertFound("paths = PackedStringArray(\"C:\\\\dir\\\\\", \"res://ui/button.tscn\")", QuoteGrammar.GodotText, Button);

    [Fact]
    public void GodotTextIgnoresALongerPath() => Assert.DoesNotContain(Button, Texts("path=\"res://ui/button.tscn.import\"", QuoteGrammar.GodotText));

    [Fact]
    public void XmlFindsAPathAfterANameEndingInABackslash() =>
        AssertFound("<x:Instance Name=\"Slot\\\" Scene=\"res://ui/button.tscn\" />", QuoteGrammar.Xml, Button);

    [Fact]
    public void XmlFindsAPathInValueTextAfterANameEndingInABackslash() =>
        AssertFound("<Label Name=\"Title\\\" Theme='Resource(\"res://themes/t.tres\")' />", QuoteGrammar.Xml, Theme);

    [Fact]
    public void XmlFindsAPathAfterAnEscapedQuoteInValueText() =>
        AssertFound("<Node Name=\"N\" Meta='[Resource(\"a\\\"b\"), Resource(\"res://themes/t.tres\")]' />", QuoteGrammar.Xml, Theme);

    [Fact]
    public void XmlFindsAUidTablePath()
    {
        const string Line = "<x:Uid Path=\"res://ui/button.tscn\" Value=\"uid://bbutt\" />";

        AssertFound(Line, QuoteGrammar.Xml, Button);
        Assert.Equal(Button, ReferenceScan.AttributeValue(Line, "Path"));
    }

    [Fact]
    public void XmlFindsAPathInASingleQuotedAttribute() =>
        AssertFound("<x:Instance Name=\"I\" Scene='res://ui/button.tscn' />", QuoteGrammar.Xml, Button);

    [Fact]
    public void XmlIgnoresAPathInsideLongerText() =>
        Assert.DoesNotContain(Button, Texts("<Label Name=\"L\" Text=\"see res://ui/button.tscn\" />", QuoteGrammar.Xml));

    [Fact]
    public void XmlIgnoresAPathInElementTextAndComments() =>
        Assert.Empty(ReferenceScan.Quoted("<!-- \"res://ui/button.tscn\" -->", QuoteGrammar.Xml));

    // The key is found as a span on line 1 at its opening quote, the character before the key.
    private static void AssertFound(string line, QuoteGrammar grammar, string key)
    {
        int column = line.IndexOf(key, StringComparison.Ordinal);
        Assert.Contains((1, column, key), ReferenceScan.Quoted(line, grammar));
    }

    private static List<string> Texts(string line, QuoteGrammar grammar) => [.. ReferenceScan.Quoted(line, grammar).Select(span => span.Text)];
}
