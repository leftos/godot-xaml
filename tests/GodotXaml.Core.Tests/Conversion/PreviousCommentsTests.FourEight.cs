using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// Godot 4.8's x:Scene and embedded scenes: their comments carry as the main tree's do, each embedded scene matched within
// itself, and one that cannot be carried warns.
public sealed partial class PreviousCommentsTests
{
    private static readonly GodotMetadata Metadata48 = GodotMetadata.LoadBundled("4.8");

    [Fact]
    public void ACommentInsideAnEmbeddedSceneIsCarried()
    {
        string previous = Embedded(innerChildComment: "<!-- about the inner child -->");

        ConversionResult<XscnDocument> forward = Carried48(previous, Embedded(innerChildComment: ""));

        Assert.DoesNotContain(forward.Issues, issue => issue.Code == ConversionCodes.CommentDropped);
        XscnNode innerChild = Assert.Single(EmbeddedRoot(forward.Document).Children.OfType<XscnNode>());
        Assert.Equal(" about the inner child ", Assert.Single(innerChild.LeadingComments));
    }

    [Fact]
    public void ACommentOnXSceneIsCarried()
    {
        string previous = $"""
            <Node {X} x:Godot="4.8" Name="Root">
              <!-- about the scene -->
              <x:Scene ResourceName="Hello" />
              <Node Name="Child" />
            </Node>
            """;
        string next = $"""
            <Node {X} x:Godot="4.8" Name="Root">
              <x:Scene ResourceName="Hello" />
              <Node Name="Child" />
            </Node>
            """;

        ConversionResult<XscnDocument> forward = Carried48(previous, next);

        Assert.DoesNotContain(forward.Issues, issue => issue.Code == ConversionCodes.CommentDropped);
        XscnResource scene = Assert.IsType<XscnResource>(forward.Document.Scene);
        Assert.Equal(" about the scene ", Assert.Single(scene.LeadingComments));
    }

    [Fact]
    public void ACommentInsideADroppedEmbeddedSceneWarnsGXC0117()
    {
        string previous = Embedded(innerChildComment: "<!-- about the inner child -->");
        string next = $"""
            <Node {X} x:Godot="4.8" Name="Root">
              <Node Name="Child" />
            </Node>
            """;

        ConversionResult<XscnDocument> forward = Carried48(previous, next);

        ConversionIssue issue = Dropped(forward);
        Assert.Equal("GXC0117", issue.Code);
        Assert.Contains("about the inner child", issue.Message, StringComparison.Ordinal);
        Assert.Contains("InnerChild", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmbeddedNodeSharingAMainNodesUniqueIdDoesNotTakeItsComment()
    {
        string previous = EmbeddedWithIds("<!-- about the inner child -->", "<!-- about the main child -->");

        ConversionResult<XscnDocument> forward = Carried48(previous, EmbeddedWithIds("", ""));

        Assert.DoesNotContain(forward.Issues, issue => issue.Code == ConversionCodes.CommentDropped);
        XscnNode innerChild = Assert.Single(EmbeddedRoot(forward.Document).Children.OfType<XscnNode>());
        Assert.Equal(" about the inner child ", Assert.Single(innerChild.LeadingComments));
        XscnNode mainChild = Assert.Single(Assert.IsType<XscnNode>(forward.Document.Root).Children.OfType<XscnNode>());
        Assert.Equal(" about the main child ", Assert.Single(mainChild.LeadingComments));
    }

    // A node moved from the main tree into an embedded scene keeps its unique id and its path, but the two scenes match
    // apart, so its comment drops.
    [Fact]
    public void AMovedNodeKeepsNoCommentAcrossScenes()
    {
        string previous = $"""
            <Node {X} x:Godot="4.8" Name="Root">
              <Node.Metadata.inner>
                <PackedScene>
                  <Node2D Name="InnerRoot" />
                </PackedScene>
              </Node.Metadata.inner>
              <!-- about moved -->
              <Node Name="Moved" x:UniqueId="7" />
            </Node>
            """;
        string next = $"""
            <Node {X} x:Godot="4.8" Name="Root">
              <Node.Metadata.inner>
                <PackedScene>
                  <Node2D Name="InnerRoot">
                    <Node Name="Moved" x:UniqueId="7" />
                  </Node2D>
                </PackedScene>
              </Node.Metadata.inner>
            </Node>
            """;

        ConversionResult<XscnDocument> forward = Carried48(previous, next);

        ConversionIssue issue = Dropped(forward);
        Assert.Contains("about moved", issue.Message, StringComparison.Ordinal);
        XscnNode moved = Assert.IsType<XscnNode>(Assert.Single(EmbeddedRoot(forward.Document).Children));
        Assert.Empty(moved.LeadingComments);
    }

    [Fact]
    public void ACommentInsideATableEntrysEmbeddedSceneIsCarried()
    {
        string previous = TableEntry("<!-- about the item -->");

        ConversionResult<XscnDocument> forward = Carried48(previous, TableEntry(""));

        Assert.DoesNotContain(forward.Issues, issue => issue.Code == ConversionCodes.CommentDropped);
        XscnResource entry = Assert.Single(forward.Document.Resources);
        XscnNode item = Assert.IsType<XscnNode>(Assert.Single(Assert.IsType<XscnNode>(entry.SceneRoot).Children));
        Assert.Equal(" about the item ", Assert.Single(item.LeadingComments));
    }

    [Fact]
    public void ACommentInsideASceneNestedInAnEmbeddedScenesInlineResourceIsCarried()
    {
        string previous = Nested("<!-- about the deep child -->");

        ConversionResult<XscnDocument> forward = Carried48(previous, Nested(""));

        Assert.DoesNotContain(forward.Issues, issue => issue.Code == ConversionCodes.CommentDropped);
        XscnNode deepRoot = EmbeddedRoot(EmbeddedRoot(forward.Document));
        XscnNode deepChild = Assert.IsType<XscnNode>(Assert.Single(deepRoot.Children));
        Assert.Equal(" about the deep child ", Assert.Single(deepChild.LeadingComments));
    }

    [Fact]
    public void ACommentOnAnXScenePropertyElementIsCarried()
    {
        string previous = WithXSceneStyle(sceneComment: "", styleComment: "<!-- about the style -->");

        ConversionResult<XscnDocument> forward = Carried48(previous, WithXSceneStyle(sceneComment: "", styleComment: ""));

        Assert.DoesNotContain(forward.Issues, issue => issue.Code == ConversionCodes.CommentDropped);
        XscnResource scene = Assert.IsType<XscnResource>(forward.Document.Scene);
        Assert.Equal(" about the style ", Assert.Single(Assert.Single(scene.Properties).LeadingComments));
    }

    [Fact]
    public void ADroppedXSceneWarnsGXC0117ForEachCommentItHeld()
    {
        string previous = WithXSceneStyle(sceneComment: "<!-- about the scene -->", styleComment: "<!-- about the style -->");
        string next = $"""
            <Node {X} x:Godot="4.8" Name="Root">
              <Node Name="Child" />
            </Node>
            """;

        ConversionResult<XscnDocument> forward = Carried48(previous, next);

        ConversionIssue[] dropped = [.. forward.Issues.Where(issue => issue.Code == ConversionCodes.CommentDropped)];
        Assert.Equal(2, dropped.Length);
        Assert.Contains(dropped, issue => issue.Message.Contains("about the scene", StringComparison.Ordinal));
        Assert.Contains(dropped, issue => issue.Message.Contains("about the style", StringComparison.Ordinal));
    }

    [Fact]
    public void APackedScenePropertyThatLosesItsSceneWarnsGXC0117()
    {
        string previous = Embedded(innerChildComment: "<!-- about the inner child -->");
        string next = $"""
            <Node {X} x:Godot="4.8" Name="Root">
              <Node.Metadata.inner>
                <PackedScene ResourceName="Hollow" />
              </Node.Metadata.inner>
              <Node Name="Child" />
            </Node>
            """;

        ConversionResult<XscnDocument> forward = Carried48(previous, next);

        ConversionIssue issue = Dropped(forward);
        Assert.Contains("about the inner child", issue.Message, StringComparison.Ordinal);
        Assert.Contains("InnerChild", issue.Message, StringComparison.Ordinal);
    }

    // A PackedScene entry used twice, so it stays in the table, whose scene's child carries the comment given.
    private static string TableEntry(string itemComment) =>
        $$"""
            <Node {{X}} x:Godot="4.8" Name="Root">
              <x:Resources>
                <PackedScene x:Key="Menu">
                  <Node Name="MenuRoot">
                    {{itemComment}}
                    <Node Name="Item" />
                  </Node>
                </PackedScene>
              </x:Resources>
              <Node Name="One" Metadata.a="{Menu}" />
              <Node Name="Two" Metadata.a="{Menu}" />
            </Node>
            """;

    // An embedded scene whose root holds another embedded scene, whose child carries the comment given.
    private static string Nested(string deepChildComment) =>
        $"""
            <Node {X} x:Godot="4.8" Name="Root">
              <Node.Metadata.inner>
                <PackedScene>
                  <Node2D Name="InnerRoot">
                    <Node2D.Metadata.deep>
                      <PackedScene>
                        <Node Name="DeepRoot">
                          {deepChildComment}
                          <Node Name="DeepChild" />
                        </Node>
                      </PackedScene>
                    </Node2D.Metadata.deep>
                  </Node2D>
                </PackedScene>
              </Node.Metadata.inner>
            </Node>
            """;

    // x:Scene with its comment, holding an inline resource in a property element with its own.
    private static string WithXSceneStyle(string sceneComment, string styleComment) =>
        $"""
            <Node {X} x:Godot="4.8" Name="Root">
              {sceneComment}
              <x:Scene>
                {styleComment}
                <x:Scene.Metadata.style>
                  <StyleBoxFlat BgColor="1, 0, 0, 1" />
                </x:Scene.Metadata.style>
              </x:Scene>
              <Node Name="Child" />
            </Node>
            """;

    private static XscnNode EmbeddedRoot(XscnNode node) =>
        Assert.IsType<XscnNode>(node.Properties.Select(property => property.Value).OfType<XscnInline>().First().Resource.SceneRoot);

    private static string Embedded(string innerChildComment) =>
        $"""
            <Node {X} x:Godot="4.8" Name="Root">
              <Node.Metadata.inner>
                <PackedScene>
                  <Node2D Name="InnerRoot">
                    {innerChildComment}
                    <Node2D Name="InnerChild" />
                  </Node2D>
                </PackedScene>
              </Node.Metadata.inner>
              <Node Name="Child" />
            </Node>
            """;

    // The embedded scene's child has the main tree's child's unique id.
    private static string EmbeddedWithIds(string innerChildComment, string mainChildComment) =>
        $"""
            <Node {X} x:Godot="4.8" Name="Root" x:UniqueId="1">
              <Node.Metadata.inner>
                <PackedScene>
                  <Node2D Name="InnerRoot" x:UniqueId="2">
                    {innerChildComment}
                    <Node2D Name="InnerChild" x:UniqueId="7" />
                  </Node2D>
                </PackedScene>
              </Node.Metadata.inner>
              {mainChildComment}
              <Node Name="Child" x:UniqueId="7" />
            </Node>
            """;

    private static XscnNode EmbeddedRoot(XscnDocument document) =>
        Assert.IsType<XscnNode>(
            Assert.IsType<XscnNode>(document.Root).Properties.Select(property => property.Value).OfType<XscnInline>().First().Resource.SceneRoot
        );

    // The previous document's comments carried onto the conversion of the next document's .tscn, all under 4.8.
    private static ConversionResult<XscnDocument> Carried48(string previousText, string nextText)
    {
        XscnDocument previous = Parse48(previousText);
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Parse48(nextText), Metadata48, Options);
        return TscnToXscn.Convert(back.Document, Metadata48, Options with { Previous = new PreviousDocument(previous, Metadata48) });
    }

    private static XscnDocument Parse48(string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata48);
        Assert.Empty(parsed.Diagnostics);
        return parsed.Document;
    }
}
