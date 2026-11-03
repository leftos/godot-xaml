namespace GodotXaml.Core.Diagnostics;

/// <summary>A line and a column of a source text, both counted from 0 or both from 1 as the method that gave it says.</summary>
/// <param name="Line">The line.</param>
/// <param name="Column">The column, in UTF-16 code units.</param>
public readonly record struct LinePosition(int Line, int Column);

/// <summary>
/// Turns a UTF-16 offset into a line and a column, and a line and a column back into an offset.
/// A line ends at a line feed, a carriage return, or the two together.
/// </summary>
public sealed class LineMap
{
    private readonly int[] lineStarts;
    private readonly string text;
    private readonly int length;

    /// <summary>Indexes the start of every line of a text.</summary>
    /// <param name="text">The text.</param>
    public LineMap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        this.text = text;
        length = text.Length;
        lineStarts = [.. LineStarts(text)];
    }

    /// <summary>How many lines the text has; an empty text has one.</summary>
    public int LineCount => lineStarts.Length;

    /// <summary>The 0-based line and column of an offset, as the language server protocol counts them.</summary>
    /// <param name="offset">The offset, from 0 to the text's length.</param>
    /// <returns>The position.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> lies outside the text.</exception>
    public LinePosition ZeroBased(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, length);
        int found = Array.BinarySearch(lineStarts, offset);
        int line = found >= 0 ? found : ~found - 1;
        return new LinePosition(line, offset - lineStarts[line]);
    }

    /// <summary>The 1-based line and column of an offset, as an error message shows them.</summary>
    /// <param name="offset">The offset, from 0 to the text's length.</param>
    /// <returns>The position.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> lies outside the text.</exception>
    public LinePosition OneBased(int offset)
    {
        LinePosition zero = ZeroBased(offset);
        return new LinePosition(zero.Line + 1, zero.Column + 1);
    }

    /// <summary>The offset of a 0-based position, the exact inverse of <see cref="ZeroBased"/>.</summary>
    /// <param name="zeroBased">The line and column, both counted from 0.</param>
    /// <returns>The offset, from 0 to the text's length.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The line or the column lies outside the text.</exception>
    public int Offset(LinePosition zeroBased)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zeroBased.Line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(zeroBased.Line, lineStarts.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(zeroBased.Column);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(zeroBased.Column, SpanEnd(zeroBased.Line) - lineStarts[zeroBased.Line]);
        return lineStarts[zeroBased.Line] + zeroBased.Column;
    }

    /// <summary>The offset of a 0-based position as the language server protocol reads one, clamped into the text.</summary>
    /// <param name="zeroBased">The line and column, both counted from 0; either may be negative or past the text.</param>
    /// <returns>The offset, from 0 to the text's length.</returns>
    public int ClampedOffset(LinePosition zeroBased)
    {
        if (zeroBased.Line >= lineStarts.Length)
        {
            return length;
        }

        int line = Math.Max(zeroBased.Line, 0);
        int column = Math.Max(zeroBased.Column, 0);
        int contentEnd = ContentEnd(line) - lineStarts[line];
        return lineStarts[line] + Math.Min(column, contentEnd);
    }

    /// <summary>The last offset a line's span takes, which is the line feed of a carriage return and line feed.</summary>
    private int SpanEnd(int line) => line == lineStarts.Length - 1 ? length : lineStarts[line + 1] - 1;

    /// <summary>The offset just past a line's content, before its line break.</summary>
    private int ContentEnd(int line)
    {
        if (line == lineStarts.Length - 1)
        {
            return length;
        }

        int breakEnd = lineStarts[line + 1];
        bool carriageReturnLineFeed = breakEnd >= 2 && text[breakEnd - 1] == '\n' && text[breakEnd - 2] == '\r';
        return carriageReturnLineFeed ? breakEnd - 2 : breakEnd - 1;
    }

    private static IEnumerable<int> LineStarts(string text)
    {
        yield return 0;
        for (int i = 0; i < text.Length; i++)
        {
            bool crBeforeLf = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n';
            if (text[i] is '\n' or '\r' && !crBeforeLf)
            {
                yield return i + 1;
            }
        }
    }
}
