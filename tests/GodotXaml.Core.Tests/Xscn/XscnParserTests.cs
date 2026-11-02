using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed class XscnParserTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    [Fact]
    public void TheRootDeclaresItsSceneNamespaces()
    {
        XscnDocument document = Parse($"""<Control {X} xmlns:c="res://ui/" xmlns:res="res://" Name="Root" />""");

        Assert.Equal([new XscnNamespace("c", "res://ui/"), new XscnNamespace("res", "res://")], document.Namespaces);
        XscnNode root = Assert.IsType<XscnNode>(document.Root);
        Assert.Equal("Control", Assert.IsType<XscnClassType>(root.Type).Class.Name);
        Assert.Equal("Root", root.Name);
        Assert.Empty(root.Properties);
    }

    [Fact]
    public void TheUidTableListsEachFileAndItsUid()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <x:Uids>
                <x:Uid Path="res://ui/Modal.tscn" Value="uid://abc" />
                <x:Uid Path="res://art/frame.png" Value="uid://def" />
              </x:Uids>
            </Control>
            """
        );

        Assert.Equal([new XscnUid("res://ui/Modal.tscn", "uid://abc"), new XscnUid("res://art/frame.png", "uid://def")], document.Uids);
        Assert.Empty(Assert.IsType<XscnNode>(document.Root).Children);
    }

    [Fact]
    public void AResourceTableEntryHasAKeyThatAnAttributeRefersTo()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <x:Resources>
                <StyleBoxFlat x:Key="Panel" BgColor="1, 0, 0, 1" />
              </x:Resources>
              <PanelContainer Name="P" ThemeOverrideStyles.panel="{"{Panel}"}" />
            </Control>
            """
        );

        XscnResource resource = Assert.Single(document.Resources);
        Assert.Equal("Panel", resource.Key);
        Assert.Equal("StyleBoxFlat", Assert.IsType<XscnClassType>(resource.Type).Class.Name);
        XscnProperty color = Assert.Single(resource.Properties);
        Assert.Equal("bg_color", color.GodotName);
        GodotReal real = Assert.IsType<GodotReal>(Assert.IsType<XscnScalar>(color.Value).Value);
        Assert.Equal([1f, 0f, 0f, 1f], real.Components);
        XscnNode panel = ChildNode(document.Root, 0);
        Assert.Equal(new XscnProperty("theme_override_styles/panel", new XscnKeyRef("Panel")), Assert.Single(panel.Properties));
    }

    [Fact]
    public void ASceneTagResolvesThroughItsNamespaceAndTheUidTable()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:c="res://ui/" Name="Root">
              <x:Uids>
                <x:Uid Path="res://ui/Modal.tscn" Value="uid://abc" />
              </x:Uids>
              <c:Modal Name="Confirm" />
              <c:Other Name="Other" />
            </Control>
            """
        );

        XscnNode modal = ChildNode(document.Root, 0);
        Assert.Equal(new XscnSceneType("res://ui/Modal.tscn"), modal.Type);
        Assert.Equal("Confirm", modal.Name);
        Assert.Equal(new XscnSceneType("res://ui/Other.xscn"), ChildNode(document.Root, 1).Type);
    }

    [Fact]
    public void AnOverrideInsideAnInstanceIsNamedByItsPath()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:c="res://ui/" Name="Root">
              <c:Modal Name="Confirm" x:Editable="True" x:Index="2">
                <x:Override Path="Centre/Panel" Visible="False">
                  <Label Name="Added" />
                </x:Override>
              </c:Modal>
            </Control>
            """
        );

        XscnNode modal = ChildNode(document.Root, 0);
        Assert.True(modal.Editable);
        Assert.Equal(2, modal.Index);
        XscnOverride change = Assert.IsType<XscnOverride>(Assert.Single(modal.Children));
        Assert.Equal("Centre/Panel", change.Path);
        Assert.Equal(new XscnProperty("visible", new XscnScalar(new GodotBool(false))), Assert.Single(change.Properties));
        Assert.Equal("Added", Assert.IsType<XscnNode>(Assert.Single(change.Children)).Name);
    }

    [Fact]
    public void APropertyElementHoldsAnInlineResource()
    {
        XscnDocument document = Parse(
            $"""
            <PanelContainer {X} Name="Root">
              <PanelContainer.ThemeOverrideStyles.panel>
                <StyleBoxFlat CornerRadiusTopLeft="4" />
              </PanelContainer.ThemeOverrideStyles.panel>
            </PanelContainer>
            """
        );

        XscnProperty property = Assert.Single(Assert.IsType<XscnNode>(document.Root).Properties);
        Assert.Equal("theme_override_styles/panel", property.GodotName);
        XscnResource inline = Assert.IsType<XscnInline>(property.Value).Resource;
        Assert.Equal("StyleBoxFlat", Assert.IsType<XscnClassType>(inline.Type).Class.Name);
        Assert.Null(inline.Key);
        Assert.Equal(new XscnProperty("corner_radius_top_left", new XscnScalar(new GodotInt(4))), Assert.Single(inline.Properties));
    }

    [Fact]
    public void APropElementNamesThePropertyVerbatimAndHoldsGodotValueText()
    {
        XscnDocument document = Parse($"""<Control {X} Name="Root"><x:Prop Name="metadata/my key" Value="1" /></Control>""");

        XscnProperty property = Assert.Single(Assert.IsType<XscnNode>(document.Root).Properties);
        Assert.Equal(new XscnProperty("metadata/my key", new XscnScalar(new GodotInt(1))), property);
    }

    [Fact]
    public void AConnectElementCarriesTheReceiverFlagsAndBinds()
    {
        XscnDocument document = Parse(
            $"""<Button {X} Name="Root"><x:Connect Signal="Pressed" To="Panel" Method="Hide" Flags="Deferred, OneShot" Binds='[1, "a"]' /></Button>"""
        );

        XscnConnection connection = Assert.Single(Assert.IsType<XscnNode>(document.Root).Connections);
        Assert.Equal("pressed", connection.Signal);
        Assert.Equal("Panel", connection.To);
        Assert.Equal("Hide", connection.Method);
        Assert.Equal(1 | 2 | 4, connection.Flags);
        Assert.Equal([new GodotInt(1), new GodotString("a")], connection.Binds);
        Assert.Equal(0, connection.Unbinds);
    }

    [Fact]
    public void ASignalAttributeIsAConnectionToTheRootWithDefaultFlags()
    {
        XscnDocument document = Parse($"""<Control {X} Name="Root"><Button Name="Update" Pressed="on_x" /></Control>""");

        XscnNode button = ChildNode(document.Root, 0);
        Assert.Empty(button.Properties);
        XscnConnection connection = Assert.Single(button.Connections);
        Assert.Equal(("pressed", ".", "on_x", 2), (connection.Signal, connection.To, connection.Method, connection.Flags));
        Assert.Empty(connection.Binds);
    }

    [Fact]
    public void ARootTaggedWithASceneInheritsIt()
    {
        XscnDocument document = Parse($"""<c:Base {X} xmlns:c="res://ui/" Name="Root" />""");

        XscnNode root = Assert.IsType<XscnNode>(document.Root);
        Assert.Equal(new XscnSceneType("res://ui/Base.xscn"), root.Type);
        Assert.Equal("Root", root.Name);
    }

    [Fact]
    public void AResourceDocumentsRootIsAResource()
    {
        XscnParseResult result = XscnParser.Parse("""<StyleBoxFlat BgColor="0, 0, 1, 1" />""", XscnDocumentKind.Resource, Real);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(XscnDocumentKind.Resource, result.Document.Kind);
        XscnResource root = Assert.IsType<XscnResource>(result.Document.Root);
        Assert.Equal("StyleBoxFlat", Assert.IsType<XscnClassType>(root.Type).Class.Name);
        Assert.Equal("bg_color", Assert.Single(root.Properties).GodotName);
    }

    [Fact]
    public void APercentMarksAUniqueName()
    {
        XscnNode root = Assert.IsType<XscnNode>(Parse("""<Button Name="%Press" />""").Root);

        Assert.Equal("Press", root.Name);
        Assert.True(root.Unique);
    }

    [Fact]
    public void ADottedAttributeIsAThemeOverride()
    {
        XscnNode root = Assert.IsType<XscnNode>(Parse("""<VBoxContainer Name="Root" ThemeOverrideConstants.separation="12" />""").Root);

        Assert.Equal(new XscnProperty("theme_override_constants/separation", new XscnScalar(new GodotInt(12))), Assert.Single(root.Properties));
    }

    [Fact]
    public void ACommentLeadsTheElementAfterItAndTrailsTheContentAtTheEnd()
    {
        XscnDocument document = Parse(
            $"""
            <!-- before root -->
            <Control {X} Name="Root">
              <!-- lead -->
              <Label Name="A" />
              <!-- tail -->
            </Control>
            """
        );

        XscnNode root = Assert.IsType<XscnNode>(document.Root);
        Assert.Equal([" before root "], root.LeadingComments);
        Assert.Equal([" lead "], ChildNode(root, 0).LeadingComments);
        Assert.Empty(ChildNode(root, 0).TrailingComments);
        Assert.Equal([" tail "], root.TrailingComments);
    }

    [Fact]
    public void ACommentBeforeAConnectStaysWithTheConnection()
    {
        XscnNode root = Assert.IsType<XscnNode>(
            Parse($"""<Button {X} Name="Root"><!-- c --><x:Connect Signal="Pressed" Method="Hide" /><Label Name="A" /></Button>""").Root
        );

        Assert.Equal([" c "], Assert.Single(root.Connections).LeadingComments);
        Assert.Empty(ChildNode(root, 0).LeadingComments);
    }

    [Fact]
    public void ACommentBeforeAPropStaysWithTheProperty()
    {
        XscnNode root = Assert.IsType<XscnNode>(
            Parse($"""<Control {X} Name="Root"><!-- p --><x:Prop Name="metadata/k" Value="1" /></Control>""").Root
        );

        Assert.Equal([" p "], Assert.Single(root.Properties).LeadingComments);
        Assert.Empty(root.TrailingComments);
    }

    [Fact]
    public void ACommentBeforeAPropertyElementStaysWithTheProperty()
    {
        XscnDocument document = Parse(
            $"""
            <PanelContainer {X} Name="Root">
              <!-- e -->
              <PanelContainer.ThemeOverrideStyles.panel>
                <StyleBoxFlat />
              </PanelContainer.ThemeOverrideStyles.panel>
            </PanelContainer>
            """
        );

        XscnNode root = Assert.IsType<XscnNode>(document.Root);
        XscnProperty property = Assert.Single(root.Properties);
        Assert.Equal([" e "], property.LeadingComments);
        Assert.Empty(Assert.IsType<XscnInline>(property.Value).Resource.LeadingComments);
        Assert.Empty(root.TrailingComments);
    }

    [Fact]
    public void ACommentBeforeAUidStaysWithTheUid()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <x:Uids>
                <!-- u -->
                <x:Uid Path="res://a.png" Value="uid://a" />
              </x:Uids>
            </Control>
            """
        );

        Assert.Equal([" u "], Assert.Single(document.Uids).LeadingComments);
        Assert.Empty(document.UidsTrailingComments);
    }

    [Fact]
    public void ACommentBeforeTheUidTableLeadsIt()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <!-- table -->
              <x:Uids>
                <x:Uid Path="res://a.png" Value="uid://a" />
              </x:Uids>
            </Control>
            """
        );

        Assert.Equal([" table "], document.UidsLeadingComments);
        Assert.Empty(Assert.IsType<XscnNode>(document.Root).TrailingComments);
    }

    [Fact]
    public void ACommentAfterTheLastUidTrailsTheTable()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <x:Uids>
                <x:Uid Path="res://a.png" Value="uid://a" />
                <!-- end -->
              </x:Uids>
            </Control>
            """
        );

        Assert.Equal([" end "], document.UidsTrailingComments);
        Assert.Empty(Assert.Single(document.Uids).LeadingComments);
    }

    [Fact]
    public void ACommentBeforeTheResourceTableLeadsIt()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <!-- table -->
              <x:Resources>
                <StyleBoxFlat x:Key="Panel" />
              </x:Resources>
            </Control>
            """
        );

        Assert.Equal([" table "], document.ResourcesLeadingComments);
        Assert.Empty(Assert.Single(document.Resources).LeadingComments);
        Assert.Empty(Assert.IsType<XscnNode>(document.Root).TrailingComments);
    }

    [Fact]
    public void ACommentAfterTheLastResourceTrailsTheTable()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <x:Resources>
                <StyleBoxFlat x:Key="Panel" />
                <!-- end -->
              </x:Resources>
            </Control>
            """
        );

        Assert.Equal([" end "], document.ResourcesTrailingComments);
        Assert.Empty(Assert.Single(document.Resources).TrailingComments);
    }

    [Fact]
    public void ACommentAfterTheHeldResourceTrailsTheProperty()
    {
        XscnNode root = Assert.IsType<XscnNode>(
            Parse($"""<Control {X} Name="Root"><Control.Theme><Theme /><!-- note --></Control.Theme></Control>""").Root
        );

        XscnProperty property = Assert.Single(root.Properties);
        Assert.Equal([" note "], property.TrailingComments);
        Assert.Empty(Assert.IsType<XscnInline>(property.Value).Resource.TrailingComments);
    }

    [Fact]
    public void RecordsWithoutCommentsEqualThemselvesWithEmptyCommentLists()
    {
        XscnDocument document = Parse(
            $"""
            <PanelContainer {X} Name="Root">
              <PanelContainer.ThemeOverrideStyles.panel><StyleBoxFlat /></PanelContainer.ThemeOverrideStyles.panel>
            </PanelContainer>
            """
        );

        XscnResource inline = Assert.IsType<XscnInline>(Assert.Single(Assert.IsType<XscnNode>(document.Root).Properties).Value).Resource;
        Assert.Equal(inline with { LeadingComments = [], TrailingComments = [] }, inline);
        XscnDocument emptied = document with
        {
            UidsLeadingComments = [],
            UidsTrailingComments = [],
            ResourcesLeadingComments = [],
            ResourcesTrailingComments = [],
        };
        Assert.Equal(emptied, document);
    }

    private static XscnDocument Parse(string text)
    {
        XscnParseResult result = XscnParser.Parse(text, XscnDocumentKind.Scene, Real);
        Assert.Empty(result.Diagnostics);
        Assert.False(result.HasErrors);
        return result.Document;
    }

    private static XscnNode ChildNode(XscnElement parent, int index) => Assert.IsType<XscnNode>(Assert.IsType<XscnNode>(parent).Children[index]);
}
