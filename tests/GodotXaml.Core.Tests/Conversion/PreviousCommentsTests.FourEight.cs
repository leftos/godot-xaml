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
    public void AnEmbeddedNodeWithTheMainRootsUniqueIdDoesNotTakeItsComment()
    {
        string previous = EmbeddedWithIds("<!-- about the inner child -->", "<!-- about the main child -->");

        ConversionResult<XscnDocument> forward = Carried48(previous, EmbeddedWithIds("", ""));

        Assert.DoesNotContain(forward.Issues, issue => issue.Code == ConversionCodes.CommentDropped);
        XscnNode innerChild = Assert.Single(EmbeddedRoot(forward.Document).Children.OfType<XscnNode>());
        Assert.Equal(" about the inner child ", Assert.Single(innerChild.LeadingComments));
        XscnNode mainChild = Assert.Single(Assert.IsType<XscnNode>(forward.Document.Root).Children.OfType<XscnNode>());
        Assert.Equal(" about the main child ", Assert.Single(mainChild.LeadingComments));
    }

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
