using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed class XscnContextTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    // Each text marks the cursor with |, and the span a completion replaces with « and »; without « », the span is empty
    // at the cursor.
    [Theory]
    [InlineData("tag name, half-typed", "<«Butt|»", XscnCursorKind.TagName, null, "Butt", null, null, "Butt")]
    [InlineData("tag name, inside a class", "<«Bu|tton» Name=\"A\" />", XscnCursorKind.TagName, null, "Button", "Button", null, "Bu")]
    [InlineData("tag name, right after <", "<Control Name=\"R\">\n  <«|»\n</Control>", XscnCursorKind.TagName, null, null, null, null, "")]
    [InlineData("tag name, scene prefix", "<Control Name=\"R\">\n  <«c:Mo|»", XscnCursorKind.TagName, "c", "Mo", null, null, "c:Mo")]
    [InlineData("between attributes", "<Button |Name=\"A\">", XscnCursorKind.BetweenAttributes, null, "Button", "Button", null, "")]
    [InlineData(
        "between attributes, nested",
        "<Control Name=\"R\">\n  <Button |/>\n</Control>",
        XscnCursorKind.BetweenAttributes,
        null,
        "Button",
        "Button",
        null,
        ""
    )]
    [InlineData("attribute name", "<Button «Mou|»", XscnCursorKind.AttributeName, null, "Button", "Button", "Mou", "Mou")]
    [InlineData(
        "attribute name, half-typed after another",
        "<Button Name=\"A\" «Te|»",
        XscnCursorKind.AttributeName,
        null,
        "Button",
        "Button",
        "Te",
        "Te"
    )]
    [InlineData(
        "attribute name, inside a full one",
        "<Button «Mouse|Filter»=\"Pass\" />",
        XscnCursorKind.AttributeName,
        null,
        "Button",
        "Button",
        "MouseFilter",
        "Mouse"
    )]
    [InlineData(
        "attribute value",
        "<Button MouseFilter=\"«Ig|nore»\">",
        XscnCursorKind.AttributeValue,
        null,
        "Button",
        "Button",
        "MouseFilter",
        "Ig"
    )]
    [InlineData("attribute value, empty", "<Button Text=\"«|»\" />", XscnCursorKind.AttributeValue, null, "Button", "Button", "Text", "")]
    [InlineData(
        "attribute value, unterminated",
        "<Button MouseFilter=\"«Ig|»",
        XscnCursorKind.AttributeValue,
        null,
        "Button",
        "Button",
        "MouseFilter",
        "Ig"
    )]
    [InlineData(
        "x: attribute name",
        "<Button " + X + " Name=\"A\" «x:Gro|»",
        XscnCursorKind.AttributeName,
        null,
        "Button",
        "Button",
        "x:Gro",
        "x:Gro"
    )]
    [InlineData(
        "x: attribute value",
        "<Button " + X + " x:Groups=\"«a, |b»\" />",
        XscnCursorKind.AttributeValue,
        null,
        "Button",
        "Button",
        "x:Groups",
        "a, "
    )]
    [InlineData(
        "attribute name on an x: element",
        "<Control " + X + " Name=\"R\">\n  <x:Instance «Sc|»",
        XscnCursorKind.AttributeName,
        "x",
        "Instance",
        null,
        "Sc",
        "Sc"
    )]
    [InlineData("property element tag", "<Button Name=\"A\"><«Button.Th|»", XscnCursorKind.TagName, null, "Button.Th", "Button", null, "Button.Th")]
    [InlineData(
        "property element tag, unclosed",
        "<Button Name=\"A\">\n  <«Button.|»",
        XscnCursorKind.TagName,
        null,
        "Button.",
        "Button",
        null,
        "Button."
    )]
    [InlineData("content text", "<Button Name=\"A\">te|xt</Button>", XscnCursorKind.Content, null, "Button", "Button", null, "")]
    [InlineData("content at the text's length", "<Button Name=\"A\">|", XscnCursorKind.Content, null, "Button", "Button", null, "")]
    [InlineData("comment in content", "<Button Name=\"A\"><!-- no|te --></Button>", XscnCursorKind.Comment, null, "Button", "Button", null, "")]
    [InlineData("comment before the root", "<!-- he|y --><Button Name=\"A\" />", XscnCursorKind.Comment, null, null, null, null, "")]
    [InlineData("end tag", "<Button Name=\"A\"></«But|ton»>", XscnCursorKind.EndTag, null, "Button", "Button", null, "But")]
    [InlineData("after the root, at the text's length", "<Button Name=\"A\" />\n|", XscnCursorKind.None, null, null, null, null, "")]
    [InlineData("offset 0", "|<Button Name=\"A\" />", XscnCursorKind.None, null, null, null, null, "")]
    [InlineData("empty text", "|", XscnCursorKind.None, null, null, null, null, "")]
    [InlineData("unknown class", "<Gadget «Na|me»=\"G\" />", XscnCursorKind.AttributeName, null, "Gadget", null, "Name", "Na")]
    public void TheCursorIsOnWhatItIsOn(
        string name,
        string marked,
        XscnCursorKind kind,
        string? elementPrefix,
        string? elementName,
        string? className,
        string? attributeName,
        string typed
    )
    {
        Assert.NotEmpty(name);
        (string text, int offset, TextSpan replace) = Unmark(marked);

        XscnCursorContext context = XscnParser.ContextAt(text, offset, Real);

        Assert.Equal(kind, context.Kind);
        Assert.Equal(elementPrefix, context.ElementPrefix);
        Assert.Equal(elementName, context.ElementName);
        Assert.Equal(className, context.Class?.Name);
        Assert.Equal(attributeName, context.AttributeName);
        Assert.Equal(replace, context.Replace);
        Assert.Equal(typed, context.Typed);
    }

    // The elements open around the cursor's element, outermost first; a tag name typed in text has its enclosing element last.
    [Theory]
    [InlineData(
        "attribute name in a grandchild's start tag",
        "<Control Name=\"R\">\n  <Button Name=\"B\">\n    <Label «Te|»",
        "Control,Button",
        "Control,Button"
    )]
    [InlineData(
        "a tag name typed inside an unclosed parent",
        "<Control Name=\"R\">\n  <Button Name=\"B\">\n    <«La|»",
        "Control,Button",
        "Control,Button"
    )]
    [InlineData("the root's own start tag", "<Control Name=\"R\" «Na|»", "", "")]
    public void TheContextNamesItsAncestors(string name, string marked, string names, string classes)
    {
        Assert.NotEmpty(name);
        (string text, int offset, _) = Unmark(marked);

        XscnCursorContext context = XscnParser.ContextAt(text, offset, Real);

        Assert.Equal(Items(names), context.Ancestors.Select(a => a.LocalName));
        Assert.Equal(Items(classes), context.Ancestors.Select(a => a.Class?.Name ?? ""));
        Assert.All(context.Ancestors, a => Assert.Null(a.Prefix));
    }

    [Fact]
    public void AnAncestorsClassIsResolvedAsTheContextsIs()
    {
        (string text, int offset, _) = Unmark("<Control Name=\"R\">\n  <Button.Theme>\n    <StyleBoxFlat Name=\"S\" «Bg|»");

        XscnCursorContext context = XscnParser.ContextAt(text, offset, Real);
        int onElement = text.IndexOf("Button.Theme", StringComparison.Ordinal) + 1;
        XscnCursorContext atElement = XscnParser.ContextAt(text, onElement, Real);

        Assert.Equal("Button.Theme", atElement.ElementName);
        Assert.Equal(atElement.Class?.Name, Assert.Single(context.Ancestors, a => a.LocalName == "Button.Theme").Class?.Name);
        Assert.Equal("Button", atElement.Class?.Name);
    }

    private static string[] Items(string joined) => joined.Split(',', StringSplitOptions.RemoveEmptyEntries);

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void AnOffsetOutsideTheTextThrows(int offset) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => XscnParser.ContextAt("<Butt>", offset, Real));

    // The text without its markers, the cursor's offset, and the replaced span.
    private static (string Text, int Offset, TextSpan Replace) Unmark(string marked)
    {
        var text = new System.Text.StringBuilder();
        int offset = -1;
        int start = -1;
        int end = -1;
        foreach (char c in marked)
        {
            switch (c)
            {
                case '|':
                    offset = text.Length;
                    break;
                case '«':
                    start = text.Length;
                    break;
                case '»':
                    end = text.Length;
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }

        TextSpan replace = start < 0 ? new TextSpan(offset, 0) : new TextSpan(start, end - start);
        return (text.ToString(), offset, replace);
    }
}
