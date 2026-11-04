namespace GodotXaml.LanguageServer.Tests;

public sealed class ClassDocsTests
{
    [Fact]
    public void TheEmbeddedDocsFileHasButtonsBrief()
    {
        var docs = ClassDocs.LoadBundled("4.7");

        Assert.NotNull(docs);
        Assert.Equal("A themed button that can contain text and an icon.", docs.Class("Button")?.Brief);
    }

    [Fact]
    public void AMinorWithNoDocsFileHasNoDocs() => Assert.Null(ClassDocs.LoadBundled("0.0"));
}
