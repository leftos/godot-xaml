using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// Rewriting a document's res:// paths: every site the model holds one, the namespaces that follow a move, and what stays.
public sealed class XscnPathRewriterTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";
    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    [Fact]
    public void RewritesAnInstanceScenePath()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:ui="res://ui/" Name="Root">
              <x:Instance Name="Button" Scene="res://ui/button.tscn" />
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://ui/button.tscn", "res://ui/button.xscn")).Document;

        Assert.Equal(new XscnSceneType("res://ui/button.xscn"), Child(rewritten, 0).Type);
        Assert.Contains("<ui:button", Write(rewritten), StringComparison.Ordinal);
    }

    [Fact]
    public void RewritesTheInheritedRootScene()
    {
        XscnDocument document = Parse($"""<x:Instance {X} xmlns:cases="res://cases/" Name="Root" Scene="res://cases/base.tscn" />""");

        XscnDocument rewritten = Rewrite(document, ("res://cases/base.tscn", "res://cases/base.xscn")).Document;

        Assert.Equal(new XscnSceneType("res://cases/base.xscn"), Root(rewritten).Type);
    }

    [Fact]
    public void RewritesABareFileProperty()
    {
        XscnDocument document = Parse($"""<TextureRect {X} Name="Root" Texture="res://art/t.png" />""");

        XscnDocument rewritten = Rewrite(document, ("res://art/t.png", "res://art/t.xres")).Document;

        Assert.Equal(new XscnFile("res://art/t.xres"), Assert.Single(Root(rewritten).Properties).Value);
        Assert.Contains("Texture=\"res://art/t.xres\"", Write(rewritten), StringComparison.Ordinal);
    }

    [Fact]
    public void RewritesAPathInsideValueText()
    {
        XscnDocument document = Parse(
            $$"""
            <Control {{X}} Name="Root">
              <x:Prop Name="metadata/list" Value='[Resource("uid://atres", "res://a.tres")]' />
              <x:Prop Name="metadata/map" Value='{"k": Resource("uid://atres", "res://a.tres")}' />
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://a.tres", "res://b.tres")).Document;

        GodotPathRef listed = Assert.IsType<GodotPathRef>(
            Assert.IsType<GodotArray>(Assert.IsType<XscnScalar>(Root(rewritten).Properties[0].Value).Value).Items[0]
        );
        Assert.Equal("res://b.tres", listed.Path);
        Assert.Equal("uid://atres", listed.Uid);
        GodotPathRef mapped = Assert.IsType<GodotPathRef>(
            Assert.IsType<GodotDictionary>(Assert.IsType<XscnScalar>(Root(rewritten).Properties[1].Value).Value).Entries[0].Value
        );
        Assert.Equal("res://b.tres", mapped.Path);
        Assert.Equal("uid://atres", mapped.Uid);
    }

    [Fact]
    public void RewritesAPathInAConnectionBind()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <Button Name="Go">
                <x:Connect Signal="Pressed" Method="OnPressed" Binds='[Resource("uid://bbind", "res://art/b.png")]' />
              </Button>
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://art/b.png", "res://art/c.png")).Document;

        GodotPathRef bind = Assert.IsType<GodotPathRef>(Assert.Single(Assert.Single(Child(rewritten, 0).Connections).Binds));
        Assert.Equal("res://art/c.png", bind.Path);
        Assert.Equal("uid://bbind", bind.Uid);
    }

    [Fact]
    public void RewritesAUidsEntryAndKeepsItsUid()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <x:Uids>
                <x:Uid Path="res://art/frame.png" Value="uid://dframe" />
              </x:Uids>
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://art/frame.png", "res://art/frame.xres")).Document;

        XscnUid uid = Assert.Single(rewritten.Uids);
        Assert.Equal("res://art/frame.xres", uid.Path);
        Assert.Equal("uid://dframe", uid.Uid);
    }

    [Fact]
    public void RewritesAnInstancePlaceholder()
    {
        XscnDocument document = Parse(
            $"""
            <Node {X} Name="Root">
              <InstancePlaceholder Name="Later" x:InstancePlaceholder="res://cases/lone_root.tscn" />
            </Node>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://cases/lone_root.tscn", "res://cases/lone_root.xscn")).Document;

        Assert.Equal("res://cases/lone_root.xscn", Child(rewritten, 0).InstancePlaceholder);
    }

    [Fact]
    public void RewritesInsideInlineAndTableResources()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <x:Resources>
                <Theme x:Key="Table" DefaultFont="res://fonts/f.tres" />
              </x:Resources>
              <Control.Theme>
                <Theme DefaultFont="res://fonts/f.tres" />
              </Control.Theme>
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://fonts/f.tres", "res://fonts/g.tres")).Document;

        Assert.Equal(new XscnFile("res://fonts/g.tres"), Assert.Single(rewritten.Resources[0].Properties).Value);
        string written = Write(rewritten);
        Assert.DoesNotContain("res://fonts/f.tres", written, StringComparison.Ordinal);
        Assert.Equal(2, written.Split("res://fonts/g.tres").Length - 1);
    }

    [Fact]
    public void MatchesWholePathsOnly()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:ui="res://ui/" Name="Root">
              <x:Instance Name="Button" Scene="res://ui/button.tscn" />
              <TextureRect Name="Backup" Texture="res://ui/button.tscn.bak" />
              <TextureRect Name="Suffix" Texture="res://ui/button.tscnx" />
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://ui/button.tscn", "res://ui/button.xscn")).Document;

        Assert.Equal(new XscnFile("res://ui/button.tscn.bak"), Assert.Single(Child(rewritten, 1).Properties).Value);
        Assert.Equal(new XscnFile("res://ui/button.tscnx"), Assert.Single(Child(rewritten, 2).Properties).Value);
    }

    [Fact]
    public void WarnsAboutUnparsedTextHoldingAPath()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:ui="res://ui/" Name="Root">
              <Control Name="Bad" Visible="res://ui/button.tscn" />
            </Control>
            """
        );

        XscnPathRewriteResult result = Rewrite(document, ("res://ui/button.tscn", "res://ui/button.xscn"));

        Assert.Equal(["Root/Bad: unparsed text holds res://ui/button.tscn; it is not rewritten."], result.Warnings);
        Assert.Equal("res://ui/button.tscn", Assert.IsType<XscnInvalid>(Assert.Single(Child(result.Document, 0).Properties).Value).Text);
    }

    [Fact]
    public void WarningsFollowDocumentOrder()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:ui="res://ui/" Name="Root">
              <x:Resources>
                <StyleBoxFlat x:Key="Panel" BorderWidthLeft="res://ui/button.tscn" />
              </x:Resources>
              <Control Name="Bad" Visible="res://ui/button.tscn" />
            </Control>
            """
        );

        XscnPathRewriteResult result = Rewrite(document, ("res://ui/button.tscn", "res://ui/button.xscn"));

        Assert.Equal(
            [
                "x:Resources[Panel]: unparsed text holds res://ui/button.tscn; it is not rewritten.",
                "Root/Bad: unparsed text holds res://ui/button.tscn; it is not rewritten.",
            ],
            result.Warnings
        );
    }

    [Fact]
    public void KeepsCommentsThroughARewrite()
    {
        XscnDocument document = Parse(
            $"""
            <!-- before root -->
            <Control {X} xmlns:ui="res://ui/" Name="Root">
              <!-- inside root -->
              <x:Instance Name="Button" Scene="res://ui/button.tscn" />
            </Control>
            """
        );

        string written = Write(Rewrite(document, ("res://ui/button.tscn", "res://ui/button.xscn")).Document);

        Assert.Contains("<!-- before root -->", written, StringComparison.Ordinal);
        Assert.Contains("<!-- inside root -->", written, StringComparison.Ordinal);
        Assert.Contains("<ui:button Name=\"Button\" />", written, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtensionOnlyRenameKeepsTheNamespaceAndTag()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:ui="res://ui/" Name="Root">
              <x:Uids>
                <x:Uid Path="res://ui/button.tscn" Value="uid://bbutt" />
              </x:Uids>
              <ui:button Name="Press" />
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://ui/button.tscn", "res://ui/button.xscn")).Document;

        XscnNamespace scope = Assert.Single(rewritten.Namespaces);
        Assert.Equal("ui", scope.Prefix);
        Assert.Equal("res://ui/", scope.Folder);
        Assert.Contains("<ui:button", Write(rewritten), StringComparison.Ordinal);
    }

    [Fact]
    public void FolderMoveRetargetsTheNamespace()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:ui="res://ui/" Name="Root">
              <x:Uids>
                <x:Uid Path="res://ui/button.tscn" Value="uid://bbutt" />
              </x:Uids>
              <ui:button Name="Press" />
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://ui/button.tscn", "res://menus/button.xscn")).Document;

        XscnNamespace scope = Assert.Single(rewritten.Namespaces);
        Assert.Equal("ui", scope.Prefix);
        Assert.Equal("res://menus/", scope.Folder);
        string written = Write(rewritten);
        Assert.Contains("xmlns:ui=\"res://menus/\"", written, StringComparison.Ordinal);
        Assert.Contains("<ui:button", written, StringComparison.Ordinal);
    }

    [Fact]
    public void FolderMoveOfOneOfTwoScenesAddsANamespace()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:ui="res://ui/" Name="Root">
              <x:Uids>
                <x:Uid Path="res://ui/a.tscn" Value="uid://aa" />
                <x:Uid Path="res://ui/b.tscn" Value="uid://bb" />
              </x:Uids>
              <ui:a Name="A" />
              <ui:b Name="B" />
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://ui/a.tscn", "res://menus/a.xscn")).Document;

        Assert.Equal(2, rewritten.Namespaces.Length);
        Assert.Equal("ui", rewritten.Namespaces[0].Prefix);
        Assert.Equal("res://ui/", rewritten.Namespaces[0].Folder);
        Assert.Equal("menus", rewritten.Namespaces[1].Prefix);
        Assert.Equal("res://menus/", rewritten.Namespaces[1].Folder);
        Assert.Contains("xmlns:menus=\"res://menus/\"", Write(rewritten), StringComparison.Ordinal);
    }

    // A namespace whose scenes moved into a folder another namespace names leaves that namespace as it is.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FolderMoveIntoAFolderWithANamespaceKeepsThatNamespace(bool uiFirst)
    {
        string scopes = uiFirst ? "xmlns:ui=\"res://ui/\" xmlns:menus=\"res://menus/\"" : "xmlns:menus=\"res://menus/\" xmlns:ui=\"res://ui/\"";
        XscnDocument document = Parse(
            $"""
            <Control {X} {scopes} Name="Root">
              <ui:a Name="A" />
              <menus:c Name="C" />
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document, ("res://ui/a.xscn", "res://menus/a.xscn")).Document;

        XscnNamespace scope = Assert.Single(rewritten.Namespaces);
        Assert.Equal("menus", scope.Prefix);
        Assert.Equal("res://menus/", scope.Folder);
        string written = Write(rewritten);
        Assert.Contains("<menus:a", written, StringComparison.Ordinal);
        Assert.Contains("<menus:c", written, StringComparison.Ordinal);
    }

    [Fact]
    public void UnusedNamespaceIsRemoved()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:ui="res://ui/" xmlns:art="res://art/" Name="Root">
              <x:Uids>
                <x:Uid Path="res://ui/button.tscn" Value="uid://bbutt" />
              </x:Uids>
              <ui:button Name="Press" />
            </Control>
            """
        );

        XscnDocument rewritten = Rewrite(document).Document;

        Assert.Equal("ui", Assert.Single(rewritten.Namespaces).Prefix);
        Assert.DoesNotContain("xmlns:art", Write(rewritten), StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesTheInputUnchanged()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} xmlns:ui="res://ui/" Name="Root">
              <x:Instance Name="Button" Scene="res://ui/button.tscn" />
            </Control>
            """
        );
        string before = Write(document);

        XscnPathRewriteResult result = Rewrite(document, ("res://ui/button.tscn", "res://ui/button.xscn"));

        Assert.Equal(before, Write(document));
        Assert.Equal(new XscnSceneType("res://ui/button.tscn"), Child(document, 0).Type);
        Assert.Equal(new XscnSceneType("res://ui/button.xscn"), Child(result.Document, 0).Type);
    }

    [Fact]
    public void ChangedIsFalseWhenNoPathMatches()
    {
        XscnDocument document = Parse($"""<TextureRect {X} Name="Root" Texture="res://art/t.png" />""");

        XscnPathRewriteResult result = Rewrite(document, ("res://art/other.png", "res://art/other.xres"));

        Assert.False(result.Changed);
    }

    [Fact]
    public void ChangedIsTrueWhenAPathMatches()
    {
        XscnDocument document = Parse($"""<TextureRect {X} Name="Root" Texture="res://art/t.png" />""");

        XscnPathRewriteResult result = Rewrite(document, ("res://art/t.png", "res://art/t.xres"));

        Assert.True(result.Changed);
    }

    [Fact]
    public void ChangedIsTrueWhenOnlyAUidPathMatches()
    {
        XscnDocument document = Parse(
            $"""
            <Control {X} Name="Root">
              <x:Uids>
                <x:Uid Path="res://art/frame.png" Value="uid://dframe" />
              </x:Uids>
            </Control>
            """
        );

        XscnPathRewriteResult result = Rewrite(document, ("res://art/frame.png", "res://art/frame.xres"));

        Assert.True(result.Changed);
    }

    private static XscnPathRewriteResult Rewrite(XscnDocument document, params (string Old, string New)[] renames) =>
        XscnPathRewriter.Rewrite(document, renames.ToDictionary(rename => rename.Old, rename => rename.New, StringComparer.Ordinal));

    private static XscnDocument Parse(string text) => XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata).Document;

    private static XscnNode Root(XscnDocument document) => Assert.IsType<XscnNode>(document.Root);

    private static XscnNode Child(XscnDocument document, int index) => Assert.IsType<XscnNode>(Root(document).Children[index]);

    private static string Write(XscnDocument document) => XscnWriter.Write(document, Metadata);
}
