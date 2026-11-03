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

    [Fact]
    public void ALineFeedTextGivesEachLinesOffset()
    {
        var map = new LineMap("ab\ncd\nef");

        Assert.Equal(3, map.LineCount);
        Assert.Equal(0, map.Offset(new LinePosition(0, 0)));
        Assert.Equal(1, map.Offset(new LinePosition(0, 1)));
        Assert.Equal(3, map.Offset(new LinePosition(1, 0)));
        Assert.Equal(4, map.Offset(new LinePosition(1, 1)));
        Assert.Equal(8, map.Offset(new LinePosition(2, 2)));
    }

    [Fact]
    public void ACarriageReturnTextGivesEachLinesOffset()
    {
        var map = new LineMap("a\rb");

        Assert.Equal(2, map.LineCount);
        Assert.Equal(0, map.Offset(new LinePosition(0, 0)));
        Assert.Equal(1, map.Offset(new LinePosition(0, 1)));
        Assert.Equal(2, map.Offset(new LinePosition(1, 0)));
        Assert.Equal(3, map.Offset(new LinePosition(1, 1)));
    }

    [Fact]
    public void ACarriageReturnLineFeedTextGivesEachLinesOffset()
    {
        var map = new LineMap("ab\r\ncd");

        Assert.Equal(2, map.LineCount);
        Assert.Equal(2, map.Offset(new LinePosition(0, 2)));
        Assert.Equal(4, map.Offset(new LinePosition(1, 0)));
        Assert.Equal(6, map.Offset(new LinePosition(1, 2)));
    }

    [Fact]
    public void TheLastLineWithoutANewlineReachesTheTextsLength()
    {
        var map = new LineMap("ab\ncd");

        Assert.Equal(5, map.Offset(new LinePosition(1, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Offset(new LinePosition(1, 3)));
        Assert.Equal(5, map.ClampedOffset(new LinePosition(1, 3)));
    }

    [Fact]
    public void AnEmptyTextIsOneLineWhoseOnlyPositionIsOffsetZero()
    {
        var map = new LineMap("");

        Assert.Equal(1, map.LineCount);
        Assert.Equal(0, map.Offset(new LinePosition(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Offset(new LinePosition(0, 1)));
        Assert.Equal(0, map.ClampedOffset(new LinePosition(0, 1)));
        Assert.Equal(0, map.ClampedOffset(new LinePosition(1, 0)));
    }

    [Fact]
    public void AColumnBetweenACarriageReturnAndItsLineFeedIsAnOffsetAndClampsToTheContentEnd()
    {
        var map = new LineMap("ab\r\ncd");

        Assert.Equal(3, map.Offset(new LinePosition(0, 3)));
        Assert.Equal(2, map.ClampedOffset(new LinePosition(0, 3)));
    }

    [Fact]
    public void AColumnPastTheEndOfALineIsRejectedAndClampsToTheContentEnd()
    {
        var map = new LineMap("abc\ndef");

        Assert.Throws<ArgumentOutOfRangeException>(() => map.Offset(new LinePosition(0, 4)));
        Assert.Equal(3, map.ClampedOffset(new LinePosition(0, 4)));
        Assert.Equal(3, map.ClampedOffset(new LinePosition(0, 99)));
    }

    [Fact]
    public void ALinePastTheEndIsRejectedAndClampsToTheTextsLength()
    {
        var map = new LineMap("abc\ndef");

        Assert.Throws<ArgumentOutOfRangeException>(() => map.Offset(new LinePosition(2, 0)));
        Assert.Equal(7, map.ClampedOffset(new LinePosition(2, 0)));
        Assert.Equal(7, map.ClampedOffset(new LinePosition(99, 5)));
    }

    [Fact]
    public void ANegativeLineOrColumnIsRejectedAndClampsToZero()
    {
        var map = new LineMap("abc\ndef");

        Assert.Throws<ArgumentOutOfRangeException>(() => map.Offset(new LinePosition(-1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Offset(new LinePosition(0, -1)));
        Assert.Equal(0, map.ClampedOffset(new LinePosition(-1, 0)));
        Assert.Equal(0, map.ClampedOffset(new LinePosition(0, -1)));
        Assert.Equal(0, map.ClampedOffset(new LinePosition(-1, -1)));
    }

    [Fact]
    public void AColumnPastASurrogatePairCountsThePairsTwoUnits()
    {
        var map = new LineMap("😀x");

        Assert.Equal(2, map.Offset(new LinePosition(0, 2)));
        Assert.Equal(3, map.Offset(new LinePosition(0, 3)));
    }

    [Fact]
    public void EveryOffsetOfAMixedTextRoundTripsThroughItsPosition()
    {
        const string Text = "a\nbc\r\nd\re";
        var map = new LineMap(Text);

        for (int offset = 0; offset <= Text.Length; offset++)
        {
            Assert.Equal(offset, map.Offset(map.ZeroBased(offset)));
        }
    }
}
