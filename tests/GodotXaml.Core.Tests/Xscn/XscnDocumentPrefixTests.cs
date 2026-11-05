using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed class XscnDocumentPrefixTests
{
    private const string Format = "https://github.com/leftos/godot-xaml/format/1";
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    // The format prefix is the root's first binding of the format namespace; a binding in a comment or on a child is not one.
    [Theory]
    [InlineData("the x prefix", $"""<Control xmlns:x="{Format}" Name="R" />""", "x")]
    [InlineData("another prefix", $"""<Control xmlns:g="{Format}" Name="R" />""", "g")]
    [InlineData("no format binding", """<Control xmlns:c="res://ui/" Name="R" />""", null)]
    [InlineData("two bindings take the first", $"""<Control xmlns:x="{Format}" xmlns:y="{Format}" Name="R" />""", "x")]
    [InlineData("a comment's binding is not the root's", $"""<!-- xmlns:x="{Format}" --><Control Name="R" />""", null)]
    [InlineData("a child's binding is not the root's", $"""<Control Name="R"><Label xmlns:x="{Format}" Name="L" /></Control>""", null)]
    public void TheDocumentRecordsTheFormatPrefix(string name, string text, string? expected)
    {
        Assert.NotEmpty(name);

        Assert.Equal(expected, Parse(text).FormatPrefix);
    }

    [Fact]
    public void AHalfTypedBodyStillGivesTheRootsPrefixes()
    {
        XscnDocument document = Parse(
            $"""
            <Control xmlns:x="{Format}" xmlns:c="res://ui/" Name="R">
              <c:
            """
        );

        Assert.Equal("x", document.FormatPrefix);
        Assert.Equal([new XscnNamespace("c", "res://ui/")], document.Namespaces);
    }

    [Fact]
    public void ARootStartTagNotClosedYet()
    {
        XscnDocument document = Parse($"<Control xmlns:x=\"{Format}\" xmlns:c=\"res://ui/\" Name=\"R\"");

        Assert.Equal("x", document.FormatPrefix);
        Assert.Equal([new XscnNamespace("c", "res://ui/")], document.Namespaces);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<")]
    public void NoRootGivesNoPrefixes(string text)
    {
        XscnDocument document = Parse(text);

        Assert.Null(document.FormatPrefix);
        Assert.Empty(document.Namespaces);
    }

    private static XscnDocument Parse(string text) => XscnParser.Parse(text, XscnDocumentKind.Scene, Real).Document;
}
