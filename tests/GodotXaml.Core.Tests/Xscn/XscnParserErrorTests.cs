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
    private const string Format = "https://github.com/leftos/godot-xaml/format/1";
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
    public void AnEmptyKeyIsAnErrorAndTheEntryIsNotRead()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control {X} Name=\"Root\">\n  <x:Resources>\n    <StyleBoxFlat x:Key=\"\" />\n  </x:Resources>\n</Control>"
        );

        Assert.Equal([("GX0108", 3, 19)], positions);
        XscnDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("<StyleBoxFlat> in x:Resources has an empty x:Key; every entry needs one.", diagnostic.Message);
        Assert.Equal(8, diagnostic.Span.Length);
        Assert.Empty(result.Document.Resources);
    }

    [Fact]
    public void TwoEmptyKeysAreTwoErrors()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control {X} Name=\"Root\">\n  <x:Resources>\n    <StyleBoxFlat x:Key=\"\" />\n"
                + "    <StyleBoxLine x:Key=\"\" />\n  </x:Resources>\n</Control>"
        );

        Assert.Equal([("GX0108", 3, 19), ("GX0108", 4, 19)], positions);
        Assert.Empty(result.Document.Resources);
    }

    [Fact]
    public void AnEmptyKeyThenAGoodOneKeepsTheGoodOne()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control {X} Name=\"Root\">\n  <x:Resources>\n    <StyleBoxFlat x:Key=\"\" />\n"
                + "    <StyleBoxLine x:Key=\"P\" />\n  </x:Resources>\n</Control>"
        );

        Assert.Equal([("GX0108", 3, 19)], positions);
        Assert.Equal("P", Assert.Single(result.Document.Resources).Key);
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
            {"version": "4.7", "godot": "4.7", "globalEnums": {}, "classes": {"Object": {"parent": null, "instantiable": true, "enums": {},
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

    [Fact]
    public void ConnectWithSignalAndGodotSignalIsAnError()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control {X} Name=\"Root\">\n  <x:Connect Signal=\"Pressed\" GodotSignal=\"Pressed\" Method=\"M\" />\n</Control>"
        );

        Assert.Equal([("GX0118", 2, 4)], positions);
        Assert.Equal("<x:Connect> has both Signal and GodotSignal; write one.", Assert.Single(result.Diagnostics).Message);
        Assert.Empty(Assert.IsType<XscnNode>(result.Document.Root).Connections);
    }

    [Fact]
    public void ConnectWithNeitherSignalNorGodotSignalIsAnError()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control {X} Name=\"Root\">\n  <x:Connect To=\"Panel\" Method=\"M\" />\n</Control>"
        );

        Assert.Equal([("GX0108", 2, 4)], positions);
        Assert.Equal("<x:Connect> needs Signal=\"…\" or GodotSignal=\"…\".", Assert.Single(result.Diagnostics).Message);
        Assert.Empty(Assert.IsType<XscnNode>(result.Document.Root).Connections);
    }

    [Fact]
    public void ASecondFormatNamespaceIsOneErrorAndTheFirstPrefixIsKept()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control {X} xmlns:y=\"{Format}\" Name=\"Root\"><x:Connect Signal=\"Pressed\" Method=\"M\" /></Control>"
        );

        Assert.Equal([("GX0110", 1, 66)], positions);
        XscnDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(XscnSeverity.Error, diagnostic.Severity);
        Assert.Equal("The format namespace is already bound to the prefix 'x'; xmlns:y is not used.", diagnostic.Message);
        Assert.Equal(55, diagnostic.Span.Length);
        // Control has no Pressed signal, so the name is kept verbatim, as a script's own signal.
        Assert.Equal("Pressed", Assert.Single(Assert.IsType<XscnNode>(result.Document.Root).Connections).Signal);
    }

    [Fact]
    public void ASecondFormatNamespaceDeclaredFirstKeepsTheFirstPrefix()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control xmlns:y=\"{Format}\" {X} Name=\"Root\"><y:Connect Signal=\"Pressed\" Method=\"M\" /></Control>"
        );

        Assert.Equal([("GX0110", 1, 66)], positions);
        Assert.Equal("The format namespace is already bound to the prefix 'y'; xmlns:x is not used.", Assert.Single(result.Diagnostics).Message);
        Assert.Equal("Pressed", Assert.Single(Assert.IsType<XscnNode>(result.Document.Root).Connections).Signal);
    }

    [Fact]
    public void TheSecondFormatPrefixIsNotDeclared()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control {X} xmlns:y=\"{Format}\" Name=\"Root\">\n  <Label Name=\"L\" y:Index=\"1\" />\n</Control>"
        );

        Assert.Equal([("GX0110", 1, 66), ("GX0103", 2, 19)], positions);
        Assert.Equal("The prefix 'y' is not declared on the root.", result.Diagnostics[1].Message);
    }

    [Fact]
    public void APropertyElementWithoutAPropertyNameIsOneError()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"""
            <Control {X} Name="Root">
              <Control.>
                <Theme />
              </Control.>
            </Control>
            """
        );

        Assert.Equal([("GX0112", 2, 4)], positions);
        Assert.Equal("<Control.> names no property; write <Control.Property>.", Assert.Single(result.Diagnostics).Message);
        Assert.Empty(Assert.IsType<XscnNode>(result.Document.Root).Properties);
    }

    [Fact]
    public void APropertyElementWithoutAPropertyNameHoldingNothingIsOneError()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"""
            <Control {X} Name="Root">
              <Control.></Control.>
            </Control>
            """
        );

        Assert.Equal([("GX0112", 2, 4)], positions);
        Assert.Equal("<Control.> names no property; write <Control.Property>.", Assert.Single(result.Diagnostics).Message);
        Assert.Empty(Assert.IsType<XscnNode>(result.Document.Root).Properties);
    }

    [Theory]
    [InlineData("Control.Theme.")]
    [InlineData("Control..a")]
    public void APropertyElementWithAnEmptySegmentIsOneError(string tag)
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control {X} Name=\"Root\">\n  <{tag}><StyleBoxFlat /></{tag}>\n</Control>"
        );

        Assert.Equal([("GX0112", 2, 4)], positions);
        Assert.Equal($"<{tag}> names no property; write <Control.Property>.", Assert.Single(result.Diagnostics).Message);
        Assert.Empty(Assert.IsType<XscnNode>(result.Document.Root).Properties);
    }

    [Fact]
    public void ASignalNamedAttributeOnAnUnknownClassIsAProperty()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse("<Gadget Name=\"G\" Pressed=\"OnPressed\" />");

        Assert.Equal([("GX0107", 1, 2), ("GX0105", 1, 27)], positions);
        XscnNode gadget = Assert.IsType<XscnNode>(result.Document.Root);
        Assert.Empty(gadget.Connections);
        Assert.Equal("pressed", Assert.Single(gadget.Properties).GodotName);
        Assert.Equal(
            "Expected True or False. To connect a signal here, use <x:Connect Signal=\"Pressed\" Method=\"OnPressed\" />.",
            result.Diagnostics[1].Message
        );
    }

    [Fact]
    public void ASignalNamedAttributeOnAnInstanceHintsAtConnect()
    {
        string text = $"<Control {X} Name=\"Root\"><x:Instance Scene=\"res://m.tscn\" Name=\"M\" Pressed=\"OnPressed\" /></Control>";
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0105", 1, text.IndexOf("OnPressed", StringComparison.Ordinal) + 1)], positions);
        XscnDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            "Expected True or False. To connect a signal here, use <x:Connect Signal=\"Pressed\" Method=\"OnPressed\" />.",
            diagnostic.Message
        );
        Assert.Equal("OnPressed".Length, diagnostic.Span.Length);
    }

    [Fact]
    public void ANonSignalBoolAttributeOnAnInstanceKeepsThePlainInvalidValueMessage()
    {
        string text = $"<Control {X} Name=\"Root\"><x:Instance Scene=\"res://m.tscn\" Name=\"M\" Visible=\"yes\" /></Control>";
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0105", 1, text.IndexOf("yes", StringComparison.Ordinal) + 1)], positions);
        Assert.Equal("Expected True or False.", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public void APlaceholderAsTheRootIsAnError()
    {
        string text = $"<InstancePlaceholder {X} Name=\"P\" x:InstancePlaceholder=\"res://c.tscn\" />";
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(text);

        Assert.Equal([("GX0119", 1, text.IndexOf("x:InstancePlaceholder", StringComparison.Ordinal) + 1)], positions);
        XscnDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("The root cannot be an instance placeholder; Godot loads no scene whose first node is one.", diagnostic.Message);
        Assert.Equal("x:InstancePlaceholder=\"res://c.tscn\"".Length, diagnostic.Span.Length);
    }

    [Fact]
    public void APlaceholderBelowTheRootIsNoError()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Node {X} Name=\"Root\"><InstancePlaceholder Name=\"P\" x:InstancePlaceholder=\"res://c.tscn\" /></Node>"
        );

        Assert.Empty(positions);
        Assert.False(result.HasErrors);
    }

    // A reader that recursed into 10,000 elements would overflow the stack, which .NET cannot catch, so the cap reports
    // once and stops.
    [Fact]
    public void ATenThousandDeepChainIsOneErrorAndATree512Deep()
    {
        (XscnParseResult result, _) = Parse(NestedControls(10_000));

        XscnDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("GX0120", diagnostic.Code);
        Assert.Equal(XscnSeverity.Error, diagnostic.Severity);
        Assert.Equal("Elements are nested deeper than 512; the reader stops here.", diagnostic.Message);
        Assert.Equal(512, TreeDepth(Assert.IsType<XscnNode>(result.Document.Root)));
    }

    [Fact]
    public void A512DeepChainIsNoError()
    {
        (XscnParseResult result, _) = Parse(NestedControls(512));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(512, TreeDepth(Assert.IsType<XscnNode>(result.Document.Root)));
    }

    [Fact]
    public void A513DeepChainIsOneErrorAndATree512Deep()
    {
        (XscnParseResult result, _) = Parse(NestedControls(513));

        Assert.Equal("GX0120", Assert.Single(result.Diagnostics).Code);
        Assert.Equal(512, TreeDepth(Assert.IsType<XscnNode>(result.Document.Root)));
    }

    [Fact]
    public void ASiblingAfterATooDeepBranchIsRead()
    {
        string text =
            "<Control Name=\"Root\">"
            + "<Control Name=\"A\">"
            + string.Concat(Enumerable.Repeat("<Control Name=\"N\">", 511))
            + string.Concat(Enumerable.Repeat("</Control>", 511))
            + "</Control>"
            + "<Label Name=\"S\" />"
            + "</Control>";
        (XscnParseResult result, _) = Parse(text);

        Assert.Equal("GX0120", Assert.Single(result.Diagnostics).Code);
        XscnNode root = Assert.IsType<XscnNode>(result.Document.Root);
        Assert.Equal(["A", "S"], root.Children.Select(child => Assert.IsType<XscnNode>(child).Name));
        Assert.Equal(512, TreeDepth(root));
    }

    [Fact]
    public void ATooDeepPropertyElementIsOneErrorAndHoldsNoResource()
    {
        string text =
            string.Concat(Enumerable.Repeat("<Control Name=\"N\">", 512))
            + "<Control.Theme><StyleBoxFlat /></Control.Theme>"
            + string.Concat(Enumerable.Repeat("</Control>", 512));
        (XscnParseResult result, _) = Parse(text);

        Assert.Equal("GX0120", Assert.Single(result.Diagnostics).Code);
        XscnNode deepest = Assert.IsType<XscnNode>(result.Document.Root);
        while (!deepest.Children.IsEmpty)
        {
            deepest = Assert.IsType<XscnNode>(Assert.Single(deepest.Children));
        }

        Assert.Empty(deepest.Properties);
    }

    // Far past the cap, the reader stops at it; the size is chosen as one GuiLabs' own parser still reads.
    [Fact]
    public void AHundredThousandDeepChainReturnsOneError()
    {
        (XscnParseResult result, _) = Parse(NestedControls(100_000));

        Assert.Equal("GX0120", Assert.Single(result.Diagnostics).Code);
        Assert.Equal(512, TreeDepth(Assert.IsType<XscnNode>(result.Document.Root)));
    }

    // A scene of nested <Control Name="N"> elements, the outermost at depth 1.
    private static string NestedControls(int elements) =>
        string.Concat(Enumerable.Repeat("<Control Name=\"N\">", elements)) + string.Concat(Enumerable.Repeat("</Control>", elements));

    // The number of elements on the longest path from the root down.
    private static int TreeDepth(XscnNodeElement element) => 1 + (element.Children.IsEmpty ? 0 : element.Children.Max(TreeDepth));

    [Fact]
    public void AMarkerOnAChildElementIsAnUnknownFormatAttribute()
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse(
            $"<Control {X} Name=\"Root\">\n  <Label Name=\"A\" x:Godot=\"4.7\" />\n</Control>"
        );

        Assert.Equal([("GX0102", 2, 19)], positions);
        Assert.Null(result.Document.Godot);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("4.7.1")]
    [InlineData("v4.7")]
    [InlineData("four.seven")]
    [InlineData("")]
    [InlineData(" 4.7")]
    public void AMalformedMarkerIsGX0121(string version)
    {
        (XscnParseResult result, List<(string, int, int)> positions) = Parse($"<Control {X} x:Godot=\"{version}\" Name=\"Root\" />");

        Assert.Equal([("GX0121", 1, 75)], positions);
        XscnDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal($"x:Godot is \"{version}\"; it names the Godot version as <major>.<minor>, such as 4.7.", diagnostic.Message);
        Assert.Equal(XscnSeverity.Error, diagnostic.Severity);
        Assert.Null(result.Document.Godot);
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
