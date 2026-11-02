namespace GodotXaml.Core.Conversion;

/// <summary>
/// Godot's <c>String::naturalnocasecmp_to</c> (core/string/ustring.cpp:508-547, 606-661), by which the text saver sorts
/// its ext_resources (resource_format_text.h:193-199, resource_format_text.cpp:1877): leading dots first, runs of
/// digits compared as numbers, other characters compared upper-cased.
/// </summary>
internal sealed class GodotNaturalOrder : IComparer<string>
{
    /// <summary>The one comparer.</summary>
    public static GodotNaturalOrder NoCase { get; } = new();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        var left = new Cursor(x);
        var right = new Cursor(y);
        return CompareDots(left, right) ?? CompareRest(left, right);
    }

    // ustring.cpp:608-621: while either string is at a dot, the one without the dot sorts after.
    private static int? CompareDots(Cursor left, Cursor right)
    {
        while (left.Current == '.' || right.Current == '.')
        {
            if (left.Take() != '.')
            {
                return 1;
            }

            if (right.Take() != '.')
            {
                return -1;
            }

            if (right.Current == '\0')
            {
                return 1;
            }

            if (left.Current == '\0')
            {
                return -1;
            }
        }

        return null;
    }

    // ustring.cpp:623-650.
    private static int CompareRest(Cursor left, Cursor right)
    {
        while (left.Current != '\0')
        {
            if (right.Current == '\0')
            {
                return 1;
            }

            int step = Step(left, right);
            if (step != 0)
            {
                return step;
            }
        }

        return right.Current != '\0' ? -1 : 0;
    }

    private static int Step(Cursor left, Cursor right)
    {
        bool leftDigit = char.IsAsciiDigit(left.Current);
        bool rightDigit = char.IsAsciiDigit(right.Current);
        if (leftDigit != rightDigit)
        {
            return leftDigit ? -1 : 1;
        }

        if (leftDigit)
        {
            return CompareNumbers(left, right);
        }

        int order = char.ToUpperInvariant(left.Take()).CompareTo(char.ToUpperInvariant(right.Take()));
        return Math.Sign(order);
    }

    // ustring.cpp:508-547: the shorter run without leading zeros is less; equal lengths compare digit by digit.
    private static int CompareNumbers(Cursor left, Cursor right)
    {
        string leftDigits = left.TakeDigits().TrimStart('0');
        string rightDigits = right.TakeDigits().TrimStart('0');
        return leftDigits.Length != rightDigits.Length
            ? leftDigits.Length.CompareTo(rightDigits.Length)
            : Math.Sign(string.CompareOrdinal(leftDigits, rightDigits));
    }

    // A position in a string that reads '\0' past the end, as the C++ walks a null-terminated buffer.
    private sealed class Cursor(string text)
    {
        private int position;

        public char Current => position < text.Length ? text[position] : '\0';

        public char Take()
        {
            char current = Current;
            position++;
            return current;
        }

        public string TakeDigits()
        {
            int start = position;
            while (char.IsAsciiDigit(Current))
            {
                position++;
            }

            return text[start..position];
        }
    }
}
