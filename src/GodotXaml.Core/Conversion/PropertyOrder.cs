using System.Collections.Immutable;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// Sorts a section's properties as Godot lists them, since a .tscn sets them in file order and some depend on it. Godot
/// walks the class chain base first, each class's own properties then its dynamic names, then script, the script's
/// exports and metadata (object.h:322-334, object.cpp:475-538). A slash-named name no list holds is a leaf class's dynamic
/// name or a script's own; it goes after script, since a script's would be lost on a node with no script yet
/// (packed_scene.cpp:494), and a section with no script loses nothing by it. The .xscn's alphabetical order cannot say the
/// order among script exports or inside a dynamic family, so those keep source order.
/// </summary>
internal sealed class PropertyOrder(GodotMetadata metadata)
{
    private const string MetadataPrefix = "metadata/";
    private const string ScriptProperty = "script";

    private readonly Dictionary<string, Dictionary<string, Rank>> tables = new(StringComparer.Ordinal);

    // The parts of Godot's list, in its order.
    private enum Tier
    {
        ClassList,
        Script,
        SlashNames,
        Exports,
        Metadata,
    }

    // Inside a class of the chain: its own list, then the theme overrides of Control or Window.
    private enum Family
    {
        OwnList,
        ThemeOverrides,
    }

    /// <summary>The properties in Godot's order; a stable sort, so a repeated name keeps its order.</summary>
    /// <param name="cls">The section's class, or null when no class is known, which keeps source order.</param>
    /// <param name="properties">The section's properties.</param>
    /// <returns>The sorted properties.</returns>
    public ImmutableArray<XscnProperty> Sort(GodotClassInfo? cls, ImmutableArray<XscnProperty> properties)
    {
        if (cls is null)
        {
            return properties;
        }

        if (!tables.TryGetValue(cls.Name, out Dictionary<string, Rank>? table))
        {
            table = Build(cls);
            tables.Add(cls.Name, table);
        }

        return [.. properties.OrderBy(property => RankOf(table, property.GodotName))];
    }

    private static Rank RankOf(Dictionary<string, Rank> listed, string name)
    {
        if (listed.TryGetValue(name, out Rank rank))
        {
            return rank;
        }

        if (name.StartsWith(MetadataPrefix, StringComparison.Ordinal))
        {
            return new Rank(Tier.Metadata);
        }

        if (name.Contains('/', StringComparison.Ordinal))
        {
            return new Rank(Tier.SlashNames);
        }

        return new Rank(name == ScriptProperty ? Tier.Script : Tier.Exports);
    }

    // The depth of Control or Window in the chain, or -1 for a class with neither.
    private static int DeclaringDepth(ImmutableArray<GodotClassInfo> chain)
    {
        for (int depth = 0; depth < chain.Length; depth++)
        {
            if (chain[depth].Name is "Control" or "Window")
            {
                return depth;
            }
        }

        return -1;
    }

    private Dictionary<string, Rank> Build(GodotClassInfo cls)
    {
        ImmutableArray<GodotClassInfo> chain = [.. metadata.ChainOf(cls).Reverse()];
        Dictionary<string, Rank> listed = new(StringComparer.Ordinal);
        for (int depth = 0; depth < chain.Length; depth++)
        {
            ImmutableArray<GodotPropertyInfo> own = chain[depth].Properties;
            for (int index = 0; index < own.Length; index++)
            {
                listed.TryAdd(own[index].Name, new Rank(Tier.ClassList, depth, Family.OwnList, index));
            }
        }

        int declaring = DeclaringDepth(chain);
        if (declaring >= 0)
        {
            ImmutableArray<GodotThemeOverrideInfo> overrides = chain[^1].ThemeOverrides;
            for (int index = 0; index < overrides.Length; index++)
            {
                listed.TryAdd(overrides[index].Name, new Rank(Tier.ClassList, declaring, Family.ThemeOverrides, index));
            }
        }

        return listed;
    }

    private readonly record struct Rank(Tier Tier, int Depth = 0, Family Family = Family.OwnList, int Index = 0) : IComparable<Rank>
    {
        public int CompareTo(Rank other) => (Tier, Depth, Family, Index).CompareTo((other.Tier, other.Depth, other.Family, other.Index));
    }
}
