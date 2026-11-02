namespace GodotXaml.Core.Tscn;

/// <summary>Mints <c>&lt;prefix&gt;_&lt;5 characters&gt;</c> ids from a caller's seed, never from a clock or a shared random.</summary>
internal static class TscnIdMinter
{
    // Resource::generate_scene_unique_id draws from ('z' - 'a') + ('9' - '0') = 34 characters, 'a' to 'y' and '0' to
    // '8' (core/io/resource.cpp:151-163), so an id never holds 'z' or '9'.
    private const string Alphabet = "abcdefghijklmnopqrstuvwxy012345678";
    private const int Length = 5;

    // Any non-zero state keeps xorshift64 going; a zero seed takes this one.
    private const ulong ZeroSeedState = 0x9E3779B97F4A7C15;

    /// <summary>The first id the seed's sequence gives that is not taken.</summary>
    /// <param name="prefix">The text before the underscore.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="taken">The ids already in use.</param>
    /// <returns>The id.</returns>
    public static string Mint(string prefix, ulong seed, IEnumerable<string> taken)
    {
        HashSet<string> used = new(taken, StringComparer.Ordinal);
        ulong state = seed == 0 ? ZeroSeedState : seed;
        while (true)
        {
            char[] chars = new char[Length];
            for (int i = 0; i < Length; i++)
            {
                state = Next(state);
                chars[i] = Alphabet[(int)(state % (ulong)Alphabet.Length)];
            }

            string id = prefix + "_" + new string(chars);
            if (!used.Contains(id))
            {
                return id;
            }
        }
    }

    private static ulong Next(ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }
}
