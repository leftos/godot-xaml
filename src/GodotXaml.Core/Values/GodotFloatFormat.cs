using System.Globalization;

namespace GodotXaml.Core.Values;

/// <summary>
/// Godot's text for a float: rtos_fix (core/variant/variant_parser.cpp:1986-2000) over String::num_scientific
/// (core/string/ustring.cpp:1619-1635) and grisu2::to_chars (thirdparty/grisu2/grisu2.h:906-930): Grisu2's digits (not
/// always the shortest, so .NET's round-trip digits differ on some values) laid out by format_buffer (grisu2.h:841-896).
/// </summary>
internal static class GodotFloatFormat
{
    // grisu2.h:926-927: fixed notation for 10^kMinExp < |v| < 10^kMaxExp, where kMaxExp is digits10 of double.
    private const int MinExponent = -4;
    private const int MaxExponent = 15;

    /// <summary>The text of a float component (a math constructor's or a packed array's element), with no ".0".</summary>
    /// <param name="value">The value.</param>
    /// <param name="compat">Whether negative infinity is written as the compat "inf_neg".</param>
    /// <returns>Godot's text for the value.</returns>
    public static string Format(double value, bool compat)
    {
        if (value == 0.0)
        {
            // variant_parser.cpp:1987: -0 is written as 0.
            return "0";
        }

        if (!double.IsFinite(value))
        {
            return NonFinite(value, compat);
        }

        // variant_parser.cpp:1996-1998: a value a float holds exactly is written with float digits; grisu2.h:907-911 writes
        // the sign, then the digits of the magnitude.
        double magnitude = Math.Abs(value);
        (string Digits, int DecimalExponent) shortest = (double)(float)value == value ? Grisu2.Digits((float)magnitude) : Grisu2.Digits(magnitude);
        string text = FormatBuffer(shortest.Digits, shortest.Digits.Length + shortest.DecimalExponent);
        return value < 0 ? "-" + text : text;
    }

    /// <summary>The text of a top-level float, which gains ".0" when it would otherwise read as an int.</summary>
    /// <param name="value">The value.</param>
    /// <param name="compat">Whether negative infinity is written as the compat "inf_neg".</param>
    /// <returns>Godot's text for the value.</returns>
    public static string FormatTopLevel(double value, bool compat)
    {
        string text = Format(value, compat);

        // variant_parser.cpp:2027-2029.
        bool keepsAsIs = text is "inf" or "-inf" or "nan" || text.AsSpan().IndexOfAny('.', 'e', 'E') >= 0;
        return keepsAsIs ? text : text + ".0";
    }

    // variant_parser.cpp:1989-1993 and String::num (ustring.cpp:1406-1417); NaN loses its sign.
    private static string NonFinite(double value, bool compat)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (value > 0)
        {
            return "inf";
        }

        return compat ? "inf_neg" : "-inf";
    }

    // grisu2.h:841-896: digits d1..dk with the decimal point n places after d1.
    private static string FormatBuffer(string digits, int n)
    {
        int k = digits.Length;
        if (k <= n && n <= MaxExponent)
        {
            return digits + new string('0', n - k);
        }

        if (0 < n && n <= MaxExponent)
        {
            return digits[..n] + "." + digits[n..];
        }

        if (MinExponent < n && n <= 0)
        {
            return "0." + new string('0', -n) + digits;
        }

        string significand = k == 1 ? digits : digits[..1] + "." + digits[1..];
        return significand + "e" + Exponent(n - 1);
    }

    // grisu2.h:810-832: the sign always, and at least two digits.
    private static string Exponent(int exponent)
    {
        string sign = exponent < 0 ? "-" : "+";
        return sign + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
    }
}
