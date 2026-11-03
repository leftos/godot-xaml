using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Docs;

public sealed class DocExampleCheckTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    private static readonly GodotMetadata Metadata = GodotMetadata.LoadBundled("4.7");

    private static readonly string CanonicalSample = File.ReadAllText(RepoFiles.PathOf("tests", "fixtures", "xscn", "canonical", "theme.xres"));

    [Fact]
    public void AValidScenePasses()
    {
        string text = $"<Control {X} Name=\"Root\" />\n";

        Assert.Null(DocExampleCheck.Check(Block(text), Metadata));
    }

    [Fact]
    public void AMalformedBlockFailsNamingTheCode()
    {
        string? failure = DocExampleCheck.Check(Block($"<Control {X} Name=\"Root\"\n"), Metadata);

        Assert.NotNull(failure);
        Assert.Contains("GX0001", failure, StringComparison.Ordinal);
        Assert.Contains("docs/guide/a.md:12", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidTagPassesOnTheCodeItNames()
    {
        string text = $"<Control {X} />\n";

        Assert.Null(DocExampleCheck.Check(Block(text, invalidCode: "GX0104"), Metadata));
    }

    [Fact]
    public void AnInvalidTagFailsWhenTheCodeIsNotProduced()
    {
        string text = $"<Control {X} Name=\"Root\" />\n";

        string? failure = DocExampleCheck.Check(Block(text, invalidCode: "GX0104"), Metadata);

        Assert.NotNull(failure);
        Assert.Contains("GX0104", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ACanonicalBlockPassesOnCanonicalText() =>
        Assert.Null(DocExampleCheck.Check(Block(CanonicalSample, canonical: true, kind: XscnDocumentKind.Resource), Metadata));

    [Fact]
    public void ACanonicalBlockFailsOnTextThatIsNotCanonical()
    {
        string collapsed = CanonicalSample.TrimEnd('\n').Replace("\n    ", " ", StringComparison.Ordinal) + "\n";

        string? failure = DocExampleCheck.Check(Block(collapsed, canonical: true, kind: XscnDocumentKind.Resource), Metadata);

        Assert.NotNull(failure);
        Assert.Contains("docs/guide/a.md:12", failure, StringComparison.Ordinal);
    }

    private static DocBlock Block(string text, bool canonical = false, string? invalidCode = null, XscnDocumentKind kind = XscnDocumentKind.Scene) =>
        new("docs/guide/a.md", 12, kind, canonical, invalidCode, text);
}
