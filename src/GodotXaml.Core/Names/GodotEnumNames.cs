using System.Collections.Immutable;

namespace GodotXaml.Core.Names;

/// <summary>Godot's C# enum member names, ported from the engine's modules/mono/editor/bindings_generator.cpp.</summary>
internal static class GodotEnumNames
{
    /// <summary>The C# names of a class enum's members.</summary>
    /// <param name="constants">The members' Godot names, in declaration order.</param>
    /// <returns>The members' C# names, in the same order.</returns>
    public static ImmutableArray<string> ClassEnumMembers(IReadOnlyList<string> constants) => ApplyPrefix(constants, DeterminePrefix(constants));

    /// <summary>The C# names of a global enum's members.</summary>
    /// <param name="enumName">The enum's Godot name.</param>
    /// <param name="constants">The members' Godot names, in declaration order.</param>
    /// <returns>The members' C# names, in the same order.</returns>
    public static ImmutableArray<string> GlobalEnumMembers(string enumName, IReadOnlyList<string> constants)
    {
        // BindingsGenerator::_populate_global_constants: the Error enum has the prefix 'ERR_' for everything except 'OK'
        // and 'FAILED', so its prefix is forced to one word.
        int prefixLength = enumName == "Error" ? 1 : DeterminePrefix(constants);
        return ApplyPrefix(constants, prefixLength);
    }

    // BindingsGenerator::_determine_enum_prefix: the number of leading words every member shares with the first, never
    // all of the first member's words. The engine crashes on an enum with no members; here it has no prefix.
    private static int DeterminePrefix(IReadOnlyList<string> constants)
    {
        if (constants.Count == 0)
        {
            return 0;
        }

        string[] frontParts = constants[0].Split('_');
        int candidateLength = frontParts.Length - 1;
        foreach (string constant in constants)
        {
            if (candidateLength == 0)
            {
                return 0;
            }

            candidateLength = SharedLength(frontParts, constant.Split('_'), candidateLength);
        }

        return candidateLength;
    }

    // The inner loop of _determine_enum_prefix: how many of the first candidateLength words parts shares with frontParts.
    private static int SharedLength(string[] frontParts, string[] parts, int candidateLength)
    {
        int i;
        for (i = 0; i < candidateLength && i < parts.Length; i++)
        {
            if (frontParts[i] != parts[i] && !IsFlagException(frontParts[i], parts[i], i == candidateLength - 1))
            {
                break;
            }
        }

        return i;
    }

    // HARDCODED in _determine_enum_prefix: some flag enums have the prefix 'FLAG_' for everything except 'FLAGS_DEFAULT'
    // (and 'METHOD_FLAG_' with 'METHOD_FLAGS_DEFAULT'), which counts as a match in the prefix's last word.
    private static bool IsFlagException(string front, string part, bool isLastWord) =>
        isLastWord && ((front == "FLAGS" && part == "FLAG") || (front == "FLAG" && part == "FLAGS"));

    // BindingsGenerator::_apply_prefix_to_enum_constants: each member without the prefix, then
    // snake_to_pascal_case(name, true). With no prefix, or a member no longer than the prefix, the member keeps its
    // whole name, the ConstantInterface's proxy name from construction.
    private static ImmutableArray<string> ApplyPrefix(IReadOnlyList<string> constants, int prefixLength)
    {
        ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>(constants.Count);
        foreach (string constant in constants)
        {
            string[] parts = constant.Split('_');
            string stripped =
                prefixLength > 0 && parts.Length > prefixLength ? string.Join('_', parts[MemberPrefixLength(parts, prefixLength)..]) : constant;
            names.Add(GodotNames.SnakeToPascalCase(stripped, inputIsUpper: true));
        }

        return names.MoveToImmutable();
    }

    // _apply_prefix_to_enum_constants: a member whose name would begin with a digit once stripped keeps the words before
    // it back to the last one that does not begin with a digit.
    private static int MemberPrefixLength(string[] parts, int prefixLength)
    {
        if (!StartsWithDigit(parts[prefixLength]))
        {
            return prefixLength;
        }

        int length;
        for (length = prefixLength - 1; length > 0; length--)
        {
            if (!StartsWithDigit(parts[length]))
            {
                break;
            }
        }

        return length;
    }

    // The engine's is_digit on the word's first character; an empty word's first character is the terminator.
    private static bool StartsWithDigit(string part) => part.Length > 0 && part[0] is >= '0' and <= '9';
}
