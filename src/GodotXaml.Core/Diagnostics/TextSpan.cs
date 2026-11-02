namespace GodotXaml.Core.Diagnostics;

/// <summary>A range of a source text, in UTF-16 code units from the start of the text.</summary>
public readonly record struct TextSpan
{
    /// <summary>Creates a span.</summary>
    /// <param name="start">The offset of the span's first unit.</param>
    /// <param name="length">The number of units the span covers.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="length"/> is negative.</exception>
    public TextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Start = start;
        Length = length;
    }

    /// <summary>The offset of the span's first unit.</summary>
    public int Start { get; }

    /// <summary>The number of units the span covers.</summary>
    public int Length { get; }

    /// <summary>The offset just past the span's last unit.</summary>
    public int End => Start + Length;
}
