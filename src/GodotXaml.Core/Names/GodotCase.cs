namespace GodotXaml.Core.Names;

/// <summary>
/// Godot's case functions, ported from the engine's core/string/ucaps.h (_find_upper, _find_lower) and
/// core/string/ustring.cpp (String::to_upper); the tables and their license are in GodotCaseTables.g.cs.
/// </summary>
internal static class GodotCase
{
    /// <summary>The upper case of a code point by Godot's caps_table (ucaps.h _find_upper).</summary>
    /// <param name="c">The code point.</param>
    /// <returns>Its upper case, or the code point itself when the table has none.</returns>
    internal static int FindUpper(int c) => Find(GodotCaseTables.UpperFrom, GodotCaseTables.UpperTo, c);

    /// <summary>The lower case of a code point by Godot's reverse_caps_table (ucaps.h _find_lower).</summary>
    /// <param name="c">The code point.</param>
    /// <returns>Its lower case, or the code point itself when the table has none.</returns>
    internal static int FindLower(int c) => Find(GodotCaseTables.LowerFrom, GodotCaseTables.LowerTo, c);

    /// <summary>The code points upper-cased one by one, as String::to_upper does (ustring.cpp).</summary>
    /// <param name="codePoints">The code points; left unchanged.</param>
    /// <returns>A new array holding each code point's FindUpper.</returns>
    internal static int[] ToUpper(int[] codePoints)
    {
        int[] upper = new int[codePoints.Length];
        for (int i = 0; i < codePoints.Length; i++)
        {
            upper[i] = FindUpper(codePoints[i]);
        }

        return upper;
    }

    // ucaps.h:3037-3075: the binary search both _find_upper and _find_lower run over their table.
    private static int Find(ReadOnlySpan<int> from, ReadOnlySpan<int> to, int c)
    {
        int low = 0;
        int high = from.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (c < from[middle])
            {
                high = middle - 1; // Search low end of array.
            }
            else if (from[middle] < c)
            {
                low = middle + 1; // Search high end of array.
            }
            else
            {
                return to[middle];
            }
        }

        return c;
    }
}
