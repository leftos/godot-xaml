namespace GodotXaml.Core.Diagnostics;

/// <summary>A line and a column of a source text, both counted from 0 or both from 1 as the method that gave it says.</summary>
/// <param name="Line">The line.</param>
/// <param name="Column">The column, in UTF-16 code units.</param>
public readonly record struct LinePosition(int Line, int Column);

/// <summary>
/// Turns a UTF-16 offset into a line and a column. A line ends at a line feed, a carriage return, or the two together.
/// </summary>
public sealed class LineMap
{
    private readonly int[] lineStarts;
    private readonly int length;

    /// <summary>Indexes the start of every line of a text.</summary>
    /// <param name="text">The text.</param>
    public LineMap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        length = text.Length;
        lineStarts = [.. LineStarts(text)];
    }

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
