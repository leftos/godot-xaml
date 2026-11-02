using System.Text;

namespace GodotXaml.Core.Values;

/// <summary>Godot's two string escapes for the text writer (core/string/ustring.cpp:4474-4496).</summary>
internal static class GodotStringEscape
{
    // ustring.cpp:4474-4488. Each replace targets a character the earlier ones do not write, so one pass over the text
    // gives the same result as Godot's chain of replaces.
    private static readonly Dictionary<char, string> CEscapes = new()
    {
        ['\\'] = "\\\\",
        ['\a'] = "\\a",
        ['\b'] = "\\b",
        ['\f'] = "\\f",
        ['\n'] = "\\n",
        ['\r'] = "\\r",
        ['\t'] = "\\t",
        ['\v'] = "\\v",
        ['\''] = "\\'",
        ['"'] = "\\\"",
    };

    // ustring.cpp:4490-4496.
    private static readonly Dictionary<char, string> MultilineEscapes = new() { ['\\'] = "\\\\", ['"'] = "\\\"" };

    /// <summary>String::c_escape, which a StringName, a NodePath and a PackedStringArray element are written with.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string CEscape(string text) => Escape(text, CEscapes);

    /// <summary>String::c_escape_multiline, which a String is written with: only the backslash and the double quote.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string CEscapeMultiline(string text) => Escape(text, MultilineEscapes);

    private static string Escape(string text, Dictionary<char, string> escapes)
    {
        var result = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (escapes.TryGetValue(c, out string? escaped))
            {
                result.Append(escaped);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
