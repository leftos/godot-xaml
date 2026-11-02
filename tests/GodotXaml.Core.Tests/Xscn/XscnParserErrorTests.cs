using System.Text;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed class XscnParserErrorTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    [Fact]
    public void AHalfTypedTagStillYieldsANode()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse("<Control Name=\"Root\">\n  <Label Name=\"A\"\n</Control>");

        Assert.Contains(("GX0001", 2, 18), positions);
        Assert.True(result.HasErrors);
        XscnNode label = Assert.IsType<XscnNode>(Assert.Single(Assert.IsType<XscnNode>(result.Document.Root).Children));
        Assert.Equal("A", label.Name);
    }

    [Fact]
    public void ACommentInsideAStartTagIsAnError()
    {
        (_, List<(string, int, int)> positions) = Parse("<Control Name=\"Root\">\n  <Label Name=\"A\" <!-- c --> />\n</Control>");

        Assert.Contains(("GX0002", 2, 19), positions);
    }

    [Fact]
    public void AnUnknownFormatElementIsAnErrorAndIsNotKept()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse($"<Control {X} Name=\"Root\">\n  <x:Bogus />\n</Control>");

        Assert.Equal([("GX0101", 2, 4)], positions);
        Assert.Empty(Assert.IsType<XscnNode>(result.Document.Root).Children);
    }

    [Fact]
    public void AnUndeclaredPrefixIsAnError()
    {
        (_, List<(string, int, int)> positions) = Parse("<Control Name=\"Root\">\n  <q:Thing Name=\"T\" />\n</Control>");

        Assert.Equal([("GX0103", 2, 4)], positions);
    }

    [Fact]
    public void ANodeWithoutANameIsAnError()
    {
        (_, List<(string, int, int)> positions) = Parse("<Control Name=\"Root\">\n  <Label />\n</Control>");

        Assert.Equal([("GX0104", 2, 4)], positions);
    }

    [Fact]
    public void ABadCompactValueIsAnErrorSpanningTheValue()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse("<Control Name=\"Root\" CustomMinimumSize=\"1, x\" />");

        Assert.Equal([("GX0105", 1, 41)], positions);
        Assert.Equal(4, Assert.Single(result.Diagnostics).Span.Length);
    }

    [Fact]
    public void AnUnknownEnumMemberIsAnError()
    {
        (_, List<(string, int, int)> positions) = Parse("<Control Name=\"Root\" MouseFilter=\"Sometimes\" />");

        Assert.Equal([("GX0106", 1, 35)], positions);
    }

    [Fact]
    public void AnUnknownClassTagIsAWarningAndTheNodeIsKept()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse("<Control Name=\"Root\">\n  <Gadget Name=\"G\" />\n</Control>");

        Assert.Equal([("GX0107", 2, 4)], positions);
        Assert.Equal(XscnSeverity.Warning, Assert.Single(result.Diagnostics).Severity);
        Assert.False(result.HasErrors);
        XscnNode gadget = Assert.IsType<XscnNode>(Assert.Single(Assert.IsType<XscnNode>(result.Document.Root).Children));
        Assert.Equal(new XscnUnknownType("Gadget"), gadget.Type);
    }

    // The reader keeps a bad reference as written and leaves the report to the XML parser, which makes one at the '&'.
    [Theory]
    [InlineData("&#0;")]
    [InlineData("&foo;")]
    [InlineData("&#xD800;")]
    [InlineData("&amp")]
    [InlineData("&#;")]
    [InlineData("&#x;")]
    [InlineData("&;")]
    [InlineData("&#12a;")]
    public void ABadReferenceInAnAttributeIsOneXmlError(string reference)
    {
        (_, List<(string, int, int)> onRoot) = Parse($"<Control Name=\"{reference}\" />");
        (_, List<(string, int, int)> onChild) = Parse($"<Control Name=\"Root\">\n  <Label Name=\"L\" Text=\"{reference}\" />\n</Control>");

        Assert.Equal([("GX0001", 1, 16)], onRoot);
        Assert.Equal([("GX0001", 2, 25)], onChild);
    }

    [Fact]
    public void ASecondUidTableIsAnErrorAndIsNotRead()
    {
        string text =
            $"<Control {X} Name=\"Root\">\n  <x:Uids />\n  <x:Uids>\n    <!-- c -->\n"
            + "    <x:Uid Path=\"res://a.png\" Value=\"uid://a\" />\n  </x:Uids>\n</Control>";
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0115", 3, 4)], positions);
        Assert.Equal("Only one <x:Uids> table is allowed; this one is not read.", Assert.Single(result.Diagnostics).Message);
        Assert.Empty(result.Document.Uids);
        Assert.Empty(result.Document.UidsTrailingComments);
    }

    [Fact]
    public void ASecondResourceTableIsAnErrorAndIsNotRead()
    {
        string text =
            $"<Control {X} Name=\"Root\">\n  <x:Resources />\n  <x:Resources>\n    <!-- c -->\n"
            + "    <StyleBoxFlat x:Key=\"P\" />\n  </x:Resources>\n</Control>";
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0115", 3, 4)], positions);
        Assert.Equal("Only one <x:Resources> table is allowed; this one is not read.", Assert.Single(result.Diagnostics).Message);
        Assert.Empty(result.Document.Resources);
        Assert.Empty(result.Document.ResourcesTrailingComments);
    }

    [Fact]
    public void ARepeatedKeyIsAnErrorAndTheFirstResourceIsKept()
    {
        string text =
            $"<Control {X} Name=\"Root\">\n  <x:Resources>\n    <StyleBoxFlat x:Key=\"P\" />\n"
            + "    <StyleBoxLine x:Key=\"P\" />\n  </x:Resources>\n</Control>";
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0116", 4, 26)], positions);
        Assert.Equal("The key 'P' is already used in x:Resources; this resource is not read.", Assert.Single(result.Diagnostics).Message);
        Assert.Equal(1, Assert.Single(result.Diagnostics).Span.Length);
        XscnResource kept = Assert.Single(result.Document.Resources);
        Assert.Equal("StyleBoxFlat", Assert.IsType<XscnClassType>(kept.Type).Class.Name);
    }

    [Fact]
    public void ARepeatedAttributeIsOneErrorAndTheFirstValueIsKept()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            "<Control Name=\"Root\">\n  <Label Name=\"L\" Text=\"a\" Text=\"b\" />\n</Control>"
        );

        Assert.Equal([("GX0113", 2, 28)], positions);
        XscnNode label = Assert.IsType<XscnNode>(Assert.Single(Assert.IsType<XscnNode>(result.Document.Root).Children));
        Assert.Equal(new XscnProperty("text", new XscnScalar(new GodotString("a"))), Assert.Single(label.Properties));
    }

    [Fact]
    public void ARepeatedUidAttributeIsOneErrorAndTheFirstValueIsKept()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control {X} Name=\"Root\">\n  <x:Uids>\n"
                + "    <x:Uid Path=\"res://a.png\" Path=\"res://b.png\" Value=\"uid://a\" />\n  </x:Uids>\n</Control>"
        );

        Assert.Equal([("GX0113", 3, 31)], positions);
        Assert.Equal("res://a.png", Assert.Single(result.Document.Uids).Path);
    }

    [Fact]
    public void AnAttributeOnTheUidTableIsAnError()
    {
        (_, List<(string, int, int)> positions) = Parse($"<Control {X} Name=\"Root\">\n  <x:Uids Bogus=\"1\" />\n</Control>");

        Assert.Equal([("GX0102", 2, 11)], positions);
    }

    [Fact]
    public void TextInsideTheUidTableIsAnError()
    {
        (_, List<(string, int, int)> positions) = Parse($"<Control {X} Name=\"Root\">\n  <x:Uids>stray</x:Uids>\n</Control>");

        Assert.Equal([("GX0003", 2, 11)], positions);
    }

    [Fact]
    public void AnAttributeOnTheResourceTableIsAnError()
    {
        (_, List<(string, int, int)> positions) = Parse($"<Control {X} Name=\"Root\">\n  <x:Resources Bogus=\"1\" />\n</Control>");

        Assert.Equal([("GX0102", 2, 16)], positions);
    }

    [Fact]
    public void TextInsideTheResourceTableIsAnError()
    {
        (_, List<(string, int, int)> positions) = Parse($"<Control {X} Name=\"Root\">\n  <x:Resources>stray</x:Resources>\n</Control>");

        Assert.Equal([("GX0003", 2, 16)], positions);
    }

    [Fact]
    public void AnEmptyNameIsAnError()
    {
        (_, List<(string, int, int)> positions) = Parse("<Control Name=\"Root\">\n  <Label Name=\"\" />\n</Control>");

        Assert.Equal([("GX0104", 2, 4)], positions);
    }

    [Fact]
    public void APercentAloneIsAnEmptyName()
    {
        (_, List<(string, int, int)> positions) = Parse("<Control Name=\"Root\">\n  <Label Name=\"%\" />\n</Control>");

        Assert.Equal([("GX0104", 2, 4)], positions);
    }

    [Fact]
    public void ANonResourceInAPropertyElementIsOneErrorAndKeepsTheCommentsBeforeIt()
    {
        string text = $"""
            <PanelContainer {X} Name="Root">
              <PanelContainer.ThemeOverrideStyles.panel>
                <!-- c -->
                <x:Prop Name="a" Value="1" />
                <StyleBoxFlat />
              </PanelContainer.ThemeOverrideStyles.panel>
            </PanelContainer>
            """;
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0112", 4, 6)], positions);
        XscnProperty property = Assert.Single(Assert.IsType<XscnNode>(result.Document.Root).Properties);
        Assert.Equal([" c "], Assert.IsType<XscnInline>(property.Value).Resource.LeadingComments);
    }

    [Fact]
    public void ANonResourceAloneInAPropertyElementIsOneError()
    {
        string text = $"""
            <PanelContainer {X} Name="Root">
              <PanelContainer.ThemeOverrideStyles.panel>
                <x:Prop Name="a" Value="1" />
              </PanelContainer.ThemeOverrideStyles.panel>
            </PanelContainer>
            """;
        (_, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0112", 3, 6)], positions);
    }

    [Fact]
    public void ANonResourceInAPropertyElementIsOneError()
    {
        string text = $"""
            <PanelContainer {X} Name="Root">
              <PanelContainer.ThemeOverrideStyles.panel>
                <Label />
              </PanelContainer.ThemeOverrideStyles.panel>
            </PanelContainer>
            """;
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0117", 3, 6)], positions);
        XscnDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(XscnSeverity.Error, diagnostic.Severity);
        Assert.Equal("<Label> is a Label, not a Resource; only a resource can be held here.", diagnostic.Message);
        XscnProperty property = Assert.Single(Assert.IsType<XscnNode>(result.Document.Root).Properties);
        Assert.Equal("Label", Assert.IsType<XscnClassType>(Assert.IsType<XscnInline>(property.Value).Resource.Type).Class.Name);
    }

    [Fact]
    public void ANonResourceInTheResourceTableIsOneError()
    {
        string text = $"""
            <Control {X} Name="Root">
              <x:Resources>
                <Node2D x:Key="a" />
              </x:Resources>
            </Control>
            """;
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0117", 3, 6)], positions);
        Assert.Equal(XscnSeverity.Error, Assert.Single(result.Diagnostics).Severity);
        Assert.Equal("Node2D", Assert.IsType<XscnClassType>(Assert.Single(result.Document.Resources).Type).Class.Name);
    }

    [Fact]
    public void ANonResourceAsTheRootOfAnXresIsOneError()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = ParseResource("<Node2D />");

        Assert.Equal([("GX0117", 1, 2)], positions);
        Assert.Equal(XscnSeverity.Error, Assert.Single(result.Diagnostics).Severity);
        Assert.Equal("Node2D", Assert.IsType<XscnClassType>(Assert.IsType<XscnResource>(result.Document.Root).Type).Class.Name);
    }

    [Fact]
    public void AnUnknownClassInAPropertyElementIsOnlyAWarning()
    {
        string text = $"""
            <PanelContainer {X} Name="Root">
              <PanelContainer.ThemeOverrideStyles.panel>
                <Gadget />
              </PanelContainer.ThemeOverrideStyles.panel>
            </PanelContainer>
            """;
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0107", 3, 6)], positions);
        Assert.Equal(XscnSeverity.Warning, Assert.Single(result.Diagnostics).Severity);
    }

    [Fact]
    public void AScenePrefixedAttributeIsAnError()
    {
        (_, List<(string, int, int)> positions) = Parse($"<Control {X} xmlns:ui=\"res://ui/\" Name=\"Root\"\n  ui:Foo=\"1\" />");

        Assert.Equal([("GX0114", 2, 3)], positions);
    }

    [Fact]
    public void NamedConnectFlagsNeedTheEnumInTheMetadata()
    {
        const string json = """
            {"godot": "4.7", "globalEnums": {}, "classes": {"Object": {"parent": null, "instantiable": true, "enums": {},
              "properties": [], "signals": [{"name": "changed", "args": []}]}}}
            """;
        var small = GodotMetadata.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        string text =
            $"<GodotObject {X} Name=\"Root\">\n  <x:Connect Signal=\"Changed\" Method=\"a\" Flags=\"Deferred\" />\n"
            + "  <x:Connect Signal=\"Changed\" Method=\"b\" Flags=\"3\" />\n</GodotObject>";
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text, small);

        Assert.Equal([("GX0105", 2, 49)], positions);
        Assert.Equal("this metadata has no ConnectFlags names; write the integer", Assert.Single(result.Diagnostics).Message);
        Assert.Equal([2, 3], Assert.IsType<XscnNode>(result.Document.Root).Connections.Select(c => c.Flags));
    }

    private static (XscnParseResult Result, List<(string Code, int Line, int Column)> Positions) Parse(string text) => Parse(text, Real);

    private static (XscnParseResult Result, List<(string Code, int Line, int Column)> Positions) Parse(string text, GodotMetadata metadata) =>
        ParseAs(text, XscnDocumentKind.Scene, metadata);

    private static (XscnParseResult Result, List<(string Code, int Line, int Column)> Positions) ParseResource(string text) =>
        ParseAs(text, XscnDocumentKind.Resource, Real);

    // The result, and each diagnostic's code and the 1-based line and column of its start, in the result's order.
    private static (XscnParseResult Result, List<(string Code, int Line, int Column)> Positions) ParseAs(
        string text,
        XscnDocumentKind kind,
        GodotMetadata metadata
    )
    {
        XscnParseResult result = XscnParser.Parse(text, kind, metadata);
        var map = new LineMap(text);
        return (result, [.. result.Diagnostics.Select(d => (d.Code, map.OneBased(d.Span.Start).Line, map.OneBased(d.Span.Start).Column))]);
    }
}
