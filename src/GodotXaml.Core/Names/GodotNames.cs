using System.Text;

namespace GodotXaml.Core.Names;

/// <summary>Godot's C# naming rules, ported from the engine's modules/mono/utils/naming_utils.cpp.</summary>
internal static class GodotNames
{
    // naming_utils.cpp:45-56, the identifiers Godot renames whole.
    private static readonly Dictionary<string, string> PascalCaseNameOverrides = new(StringComparer.Ordinal)
    {
        ["BitMap"] = "Bitmap",
        ["JSONRPC"] = "JsonRpc",
        ["Object"] = "GodotObject",
        ["OpenXRIPBinding"] = "OpenXRIPBinding",
        ["SkeletonModification2DCCDIK"] = "SkeletonModification2DCcdik",
        ["SkeletonModification2DFABRIK"] = "SkeletonModification2DFabrik",
        ["SkeletonModification3DCCDIK"] = "SkeletonModification3DCcdik",
        ["SkeletonModification3DFABRIK"] = "SkeletonModification3DFabrik",
        ["System"] = "System_",
        ["Thread"] = "GodotThread",
    };

    // naming_utils.cpp:59-86, the parts Godot renames inside an identifier.
    private static readonly Dictionary<string, string> PascalCasePartOverrides = new(StringComparer.Ordinal)
    {
        ["AA"] = "AA", // Anti Aliasing
        ["AO"] = "AO", // Ambient Occlusion
        ["FILENAME"] = "FileName",
        ["FADEIN"] = "FadeIn",
        ["FADEOUT"] = "FadeOut",
        ["FX"] = "FX",
        ["GI"] = "GI", // Global Illumination
        ["GZIP"] = "GZip",
        ["HBOX"] = "HBox", // Horizontal Box
        ["ID"] = "Id",
        ["IO"] = "IO", // Input/Output
        ["IP"] = "IP", // Internet Protocol
        ["IV"] = "IV", // Initialization Vector
        ["MACOS"] = "MacOS",
        ["NODEPATH"] = "NodePath",
        ["SPIRV"] = "SpirV",
        ["STDIN"] = "StdIn",
        ["STDOUT"] = "StdOut",
        ["USERNAME"] = "UserName",
        ["UV"] = "UV",
        ["UV2"] = "UV2",
        ["VBOX"] = "VBox", // Vertical Box
        ["WHITESPACE"] = "WhiteSpace",
        ["WM"] = "WM",
        ["XR"] = "XR",
        ["XRAPI"] = "XRApi",
    };

    /// <summary>The C# name Godot gives a snake_case identifier.</summary>
    /// <param name="name">The identifier, split on its underscores.</param>
    /// <param name="inputIsUpper">Whether its letters are already upper case (the C++ p_input_is_upper).</param>
    /// <returns>The identifier's C# name.</returns>
    public static string SnakeToPascalCase(string name, bool inputIsUpper)
    {
        var result = new List<int>();

        // The underscore is ASCII, so splitting the UTF-16 string never splits a surrogate pair.
        int[][] parts = Array.ConvertAll(name.Split('_'), ToCodePoints);

        for (int i = 0; i < parts.Length; i++)
        {
            string? partOverride = GetPartOverride(parts[i], inputIsUpper);
            if (!string.IsNullOrEmpty(partOverride))
            {
                // Use hardcoded value for part.
                result.AddRange(ToCodePoints(partOverride));
                continue;
            }

            if (parts[i].Length > 0)
            {
                result.AddRange(UppercaseWord(parts[i], inputIsUpper));
                continue;
            }

            result.AddRange(ToCodePoints(EmptyPartUnderscores(parts, i)));
        }

        return FromCodePoints([.. result]);
    }

    /// <summary>The C# name Godot gives a PascalCase identifier.</summary>
    /// <param name="name">The identifier, split into its words where its case changes.</param>
    /// <returns>The identifier's C# name.</returns>
    public static string PascalToPascalCase(string name)
    {
        int[] identifier = ToCodePoints(name);
        if (identifier.Length == 0)
        {
            return name;
        }

        if (identifier.Length <= 2)
        {
            return FromCodePoints(GodotCase.ToUpper(identifier));
        }

        if (PascalCaseNameOverrides.TryGetValue(name, out string? renamed))
        {
            // Use hardcoded value for the identifier.
            return renamed;
        }

        var result = new List<int>();
        foreach (int[] part in SplitPascalCase(identifier))
        {
            result.AddRange(NormalizePart(part));
        }

        return FromCodePoints([.. result]);
    }

    // naming_utils.cpp:88-100: the part's hardcoded name, or null when it has none; a part that is not already upper
    // case is uppercased before the lookup.
    private static string? GetPartOverride(int[] part, bool inputIsUpper)
    {
        string key = FromCodePoints(inputIsUpper ? part : GodotCase.ToUpper(part));
        return PascalCasePartOverrides.TryGetValue(key, out string? renamed) ? renamed : null;
    }

    // naming_utils.cpp:210-223: the first character upper case, the ones after a digit upper case, and the rest lowered
    // when the input is upper case.
    private static int[] UppercaseWord(int[] part, bool inputIsUpper)
    {
        int[] chars = (int[])part.Clone();
        chars[0] = GodotCase.FindUpper(chars[0]);
        for (int i = 1; i < chars.Length; i++)
        {
            if (IsDigit(chars[i - 1]))
            {
                // Use uppercase after digits.
                chars[i] = GodotCase.FindUpper(chars[i]);
                continue;
            }

            if (inputIsUpper)
            {
                chars[i] = GodotCase.FindLower(chars[i]);
            }
        }

        return chars;
    }

    // naming_utils.cpp:224-236: the underscores of an empty part, which are kept as the identifier had them.
    private static string EmptyPartUnderscores(int[][] parts, int index)
    {
        if (index == 0 || index == parts.Length - 1)
        {
            // Preserve underscores at the beginning and end.
            return "_";
        }

        // Preserve contiguous underscores.
        return parts[index - 1].Length > 0 ? "__" : "_";
    }

    // naming_utils.cpp:163-191: one word of an identifier, renamed by the part overrides or brought to PascalCase.
    private static int[] NormalizePart(int[] part)
    {
        string? partOverride = GetPartOverride(part, inputIsUpper: true);
        if (!string.IsNullOrEmpty(partOverride))
        {
            // Use hardcoded value for part.
            return ToCodePoints(partOverride);
        }

        if (part.Length <= 2 && GodotCase.ToUpper(part).AsSpan().SequenceEqual(part))
        {
            // An acronym of length 1 or 2 keeps its case.
            return part;
        }

        // naming_utils.cpp:180-189: the same rules as a snake_case word's, which lower the rest unconditionally.
        return UppercaseWord(part, inputIsUpper: true);
    }

    // naming_utils.cpp:102-143: the identifier's words, as the case changes between them.
    private static List<int[]> SplitPascalCase(int[] identifier)
    {
        var parts = new List<int[]>();
        int start = 0;
        bool prevWasUpper = IsAsciiUpper(identifier[0]);

        for (int i = 1; i < identifier.Length; i++)
        {
            if (prevWasUpper)
            {
                StepUpperRun(identifier, parts, ref start, ref prevWasUpper, i);
            }
            else
            {
                StepLowerRun(identifier, parts, ref start, ref prevWasUpper, i);
            }
        }

        // Add the rest of the identifier as the last part.
        if (start != identifier.Length)
        {
            parts.Add(identifier[start..]);
        }

        return parts;
    }

    // naming_utils.cpp:106-127: the character at i while the one before it was upper case. A lower-case character hands
    // the run's last upper-case character to the next word: the C++'s i-- (naming_utils.cpp:116-120) puts the split
    // before that character, and the loop's i++ then re-reads the original lower-case character, whose case rules find no
    // boundary, so the part it would start is never made.
    private static void StepUpperRun(int[] identifier, List<int[]> parts, ref int start, ref bool prevWasUpper, int i)
    {
        bool digit = IsDigit(identifier[i]);
        if (!digit && !IsAsciiLower(identifier[i]))
        {
            // Another upper-case character: the word goes on.
            return;
        }

        if (!digit && i - start == 1)
        {
            // Upper character was only the beginning of a word.
            prevWasUpper = false;
            return;
        }

        int boundary = digit ? i : i - 1;
        parts.Add(identifier[start..boundary]);
        start = boundary;
        prevWasUpper = false;
    }

    // naming_utils.cpp:128-134: the character at i while the one before it was not upper case.
    private static void StepLowerRun(int[] identifier, List<int[]> parts, ref int start, ref bool prevWasUpper, int i)
    {
        if (!IsDigit(identifier[i]) && !IsAsciiUpper(identifier[i]))
        {
            return;
        }

        parts.Add(identifier[start..i]);
        start = i;
        prevWasUpper = true;
    }

    // The engine's ASCII checks, which is what naming_utils.cpp splits on (core/string/ustring.h).
    private static bool IsDigit(int value) => value is >= '0' and <= '9';

    private static bool IsAsciiUpper(int value) => value is >= 'A' and <= 'Z';

    private static bool IsAsciiLower(int value) => value is >= 'a' and <= 'z';

    // Godot's String holds code points, decoded as ustring.cpp:2093-2122 (String::append_utf16) does: a lead surrogate
    // followed by a trail surrogate is one code point, and an unpaired surrogate is a code point of its own, so every
    // length, index and substring above is Godot's.
    private static int[] ToCodePoints(string value)
    {
        var codePoints = new List<int>(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                codePoints.Add(char.ConvertToUtf32(value[i], value[i + 1]));
                i++;
                continue;
            }

            codePoints.Add(value[i]);
        }

        return [.. codePoints];
    }

    // The code points back as UTF-16: one above U+FFFF as a surrogate pair, any other as one char, so an unpaired
    // surrogate comes back as it came in.
    private static string FromCodePoints(int[] codePoints)
    {
        var text = new StringBuilder(codePoints.Length);
        foreach (int codePoint in codePoints)
        {
            if (codePoint > 0xFFFF)
            {
                int offset = codePoint - 0x10000;
                text.Append((char)(0xD800 + (offset >> 10))).Append((char)(0xDC00 + (offset & 0x3FF)));
                continue;
            }

            text.Append((char)codePoint);
        }

        return text.ToString();
    }
}
