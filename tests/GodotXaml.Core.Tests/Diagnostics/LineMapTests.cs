using GodotXaml.Core.Diagnostics;

namespace GodotXaml.Core.Tests.Diagnostics;

public sealed class LineMapTests
{
    [Fact]
    public void TheFirstOffsetIsLineZeroColumnZeroAndLineOneColumnOne()
    {
        var map = new LineMap("abc");

        Assert.Equal(new LinePosition(0, 0), map.ZeroBased(0));
        Assert.Equal(new LinePosition(1, 1), map.OneBased(0));
    }

    [Fact]
    public void AnOffsetAfterALineFeedIsOnTheNextLine()
    {
        var map = new LineMap("ab\ncd\nef");

        Assert.Equal(new LinePosition(1, 1), map.ZeroBased(4));
        Assert.Equal(new LinePosition(3, 2), map.OneBased(7));
    }

    [Fact]
    public void ACarriageReturnLineFeedIsOneLineBreak()
    {
        var map = new LineMap("ab\r\ncd");

        Assert.Equal(new LinePosition(0, 2), map.ZeroBased(2));
        Assert.Equal(new LinePosition(0, 3), map.ZeroBased(3));
        Assert.Equal(new LinePosition(1, 0), map.ZeroBased(4));
    }

    [Fact]
    public void ALoneCarriageReturnIsALineBreak()
    {
        var map = new LineMap("a\rb");

        Assert.Equal(new LinePosition(1, 0), map.ZeroBased(2));
    }

    [Fact]
    public void TheEndOfTheTextHasAPosition()
    {
        var map = new LineMap("ab\n");

        Assert.Equal(new LinePosition(1, 0), map.ZeroBased(3));
    }

    [Fact]
    public void AColumnCountsUtf16Units()
    {
        var map = new LineMap("😀x");

        Assert.Equal(new LinePosition(0, 2), map.ZeroBased(2));
    }

    [Fact]
    public void AnOffsetOutsideTheTextIsRejected()
    {
        var map = new LineMap("ab");

        Assert.Throws<ArgumentOutOfRangeException>(() => map.ZeroBased(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ZeroBased(-1));
    }
}
