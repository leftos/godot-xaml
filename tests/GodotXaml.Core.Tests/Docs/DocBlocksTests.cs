using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Docs;

public sealed class DocBlocksTests
{
    [Fact]
    public void ASceneBlockIsExtractedWithItsLineAndText()
    {
        const string Markdown = """
            # Guide

            ```xml xscn
            <Control Name="Root" />
            ```

            Text.
            """;

        DocBlock block = Assert.Single(DocBlocks.Extract(Markdown, "docs/guide/a.md"));

        Assert.Equal("docs/guide/a.md", block.Path);
        Assert.Equal(4, block.Line);
        Assert.Equal(XscnDocumentKind.Scene, block.Kind);
        Assert.False(block.Canonical);
        Assert.Null(block.InvalidCode);
        Assert.Equal("<Control Name=\"Root\" />\n", block.Text);
    }

    [Fact]
    public void AResourceBlockWithCanonicalMustBeCanonical()
    {
        const string Markdown = """
            ```xml xres canonical
            <StyleBoxFlat BgColor="1, 1, 1, 1" />
            ```
            """;

        DocBlock block = Assert.Single(DocBlocks.Extract(Markdown, "docs/guide/a.md"));

        Assert.Equal(XscnDocumentKind.Resource, block.Kind);
        Assert.True(block.Canonical);
        Assert.Null(block.InvalidCode);
        Assert.Equal(2, block.Line);
    }

    [Fact]
    public void AnInvalidTagCarriesTheCode()
    {
        const string Markdown = """
            ```xml xscn invalid GX0104
            <Control />
            ```
            """;

        DocBlock block = Assert.Single(DocBlocks.Extract(Markdown, "docs/guide/a.md"));

        Assert.False(block.Canonical);
        Assert.Equal("GX0104", block.InvalidCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("xml")]
    [InlineData("xscn")]
    [InlineData("sh")]
    public void ABlockWithoutATaggedDocumentKindIsIgnored(string info)
    {
        string markdown = $"```{info}\n<Control Name=\"Root\" />\n```\n";

        Assert.Empty(DocBlocks.Extract(markdown, "docs/guide/a.md"));
    }

    [Fact]
    public void AnUnknownTagFailsNamingIt()
    {
        const string Markdown = """
            ```xml xscn canonicall
            <Control Name="Root" />
            ```
            """;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DocBlocks.Extract(Markdown, "docs/guide/a.md"));

        Assert.Contains("canonicall", error.Message, StringComparison.Ordinal);
        Assert.Contains("docs/guide/a.md:1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIndentedFenceIsDeIndentedByItsOwnIndentation()
    {
        const string Markdown = """
            - Step:
               ```xml xscn
               <Control Name="Root" />
               ```
            """;

        DocBlock block = Assert.Single(DocBlocks.Extract(Markdown, "docs/guide/a.md"));

        Assert.Equal(3, block.Line);
        Assert.Equal("<Control Name=\"Root\" />\n", block.Text);
    }

    [Fact]
    public void AnUnclosedFenceFailsNamingItsPathAndLine()
    {
        const string Markdown = """
            ```xml xscn
            <Control Name="Root" />
            """;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DocBlocks.Extract(Markdown, "docs/guide/a.md"));

        Assert.Contains("docs/guide/a.md:1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATextWithNoFenceHoldsNoBlock() => Assert.Empty(DocBlocks.Extract("# Guide\n\nNo examples here.\n", "docs/guide/a.md"));

    [Fact]
    public void EveryBlockOfATextIsExtractedInOrder()
    {
        const string Markdown = """
            ```xml xscn
            <Control Name="Root" />
            ```

            ```xml xres
            <StyleBoxFlat />
            ```
            """;

        IReadOnlyList<DocBlock> blocks = DocBlocks.Extract(Markdown, "docs/guide/a.md");

        Assert.Equal(2, blocks.Count);
        Assert.Equal(2, blocks[0].Line);
        Assert.Equal(XscnDocumentKind.Scene, blocks[0].Kind);
        Assert.Equal(6, blocks[1].Line);
        Assert.Equal(XscnDocumentKind.Resource, blocks[1].Kind);
    }
}
