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
        var result = new StringBuilder();
        string[] parts = name.Split('_');

        for (int i = 0; i < parts.Length; i++)
        {
            string? partOverride = GetPartOverride(parts[i], inputIsUpper);
            if (!string.IsNullOrEmpty(partOverride))
            {
                // Use hardcoded value for part.
                result.Append(partOverride);
                continue;
            }

            if (parts[i].Length > 0)
            {
                result.Append(UppercaseWord(parts[i], inputIsUpper));
                continue;
            }

            result.Append(EmptyPartUnderscores(parts, i));
        }

        return result.ToString();
    }

    /// <summary>The C# name Godot gives a PascalCase identifier.</summary>
    /// <param name="name">The identifier, split into its words where its case changes.</param>
    /// <returns>The identifier's C# name.</returns>
    public static string PascalToPascalCase(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }

        if (name.Length <= 2)
        {
            return ToUpper(name);
        }

        if (PascalCaseNameOverrides.TryGetValue(name, out string? renamed))
        {
            // Use hardcoded value for the identifier.
            return renamed;
        }

        var result = new StringBuilder();
        foreach (string part in SplitPascalCase(name))
        {
            result.Append(NormalizePart(part));
        }

        return result.ToString();
    }

    // naming_utils.cpp:88-100: the part's hardcoded name, or null when it has none; a part that is not already upper
    // case is uppercased before the lookup.
    private static string? GetPartOverride(string part, bool inputIsUpper)
    {
        string key = inputIsUpper ? part : ToUpper(part);
        return PascalCasePartOverrides.TryGetValue(key, out string? renamed) ? renamed : null;
    }

    // naming_utils.cpp:210-223: the first character upper case, the ones after a digit upper case, and the rest lowered
    // when the input is upper case.
    private static string UppercaseWord(string part, bool inputIsUpper)
    {
        char[] chars = part.ToCharArray();
        chars[0] = FindUpper(chars[0]);
        for (int i = 1; i < chars.Length; i++)
        {
            if (IsDigit(chars[i - 1]))
            {
                // Use uppercase after digits.
                chars[i] = FindUpper(chars[i]);
                continue;
            }

            if (inputIsUpper)
            {
                chars[i] = FindLower(chars[i]);
            }
        }

        return new string(chars);
    }

    // naming_utils.cpp:224-236: the underscores of an empty part, which are kept as the identifier had them.
    private static string EmptyPartUnderscores(string[] parts, int index)
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
    private static string NormalizePart(string part)
    {
        string? partOverride = GetPartOverride(part, inputIsUpper: true);
        if (!string.IsNullOrEmpty(partOverride))
        {
            // Use hardcoded value for part.
            return partOverride;
        }

        if (part.Length <= 2 && ToUpper(part) == part)
        {
            // An acronym of length 1 or 2 keeps its case.
            return part;
        }

        // naming_utils.cpp:180-189: the same rules as a snake_case word's, which lower the rest unconditionally.
        return UppercaseWord(part, inputIsUpper: true);
    }

    // naming_utils.cpp:102-143: the identifier's words, as the case changes between them.
    private static List<string> SplitPascalCase(string identifier)
    {
        var parts = new List<string>();
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
    private static void StepUpperRun(string identifier, List<string> parts, ref int start, ref bool prevWasUpper, int i)
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
    private static void StepLowerRun(string identifier, List<string> parts, ref int start, ref bool prevWasUpper, int i)
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
    private static bool IsDigit(char value) => value is >= '0' and <= '9';

    private static bool IsAsciiUpper(char value) => value is >= 'A' and <= 'Z';

    private static bool IsAsciiLower(char value) => value is >= 'a' and <= 'z';

    // naming_utils.cpp:33 includes core/string/ucaps.h; _find_upper and _find_lower there are the Unicode simple case
    // mappings, which are the invariant ones.
    private static char FindUpper(char value) => char.ToUpperInvariant(value);

    private static char FindLower(char value) => char.ToLowerInvariant(value);

    private static string ToUpper(string value) => value.ToUpperInvariant();
}
