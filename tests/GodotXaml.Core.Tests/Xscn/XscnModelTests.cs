using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed class XscnModelTests
{
    [Fact]
    public void AConnectionRejectsAnUninitialisedArray() =>
        Assert.Throws<ArgumentException>(() => new XscnConnection("pressed", ".", "OnPressed", 2, default, 0, [], []));

    [Fact]
    public void ADocumentRejectsAnUninitialisedArray()
    {
        var root = new XscnNode { Type = new XscnUnknownType("Thing"), Name = "Root" };

        Assert.Throws<ArgumentException>(() => new XscnDocument(XscnDocumentKind.Scene, [], default, [], root));
    }

    [Fact]
    public void ANodeRejectsAnUninitialisedArray()
    {
        var node = new XscnNode { Type = new XscnUnknownType("Thing"), Name = "Root" };

        Assert.Throws<ArgumentException>(() => node with { Children = default });
    }
}
