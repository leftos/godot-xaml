using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// Sorts a section's properties as Godot lists them, since a .tscn sets them in file order and some depend on it. Godot
/// walks the class chain base first, each class's own properties then its dynamic names, then script, the script's
/// exports and metadata (object.h, the GDCLASS macro's _get_property_listv override; object.cpp,
/// Object::get_property_list). A name the dynamic-family table (<see cref="DynamicPropertyFamilies"/>) places goes in its
/// declaring class's place. Any other slash-named name no list holds is a script's own (or a family the table lacks); it
/// goes after script, since a script's would be lost on a node with no script yet (packed_scene.cpp,
/// SceneState::instantiate), and a section with no script loses nothing by it. The .xscn's alphabetical order cannot say
/// the order among script exports or inside a dynamic family, so those keep source order, except a family whose entries
/// Godot creates on one key (Animation tracks, Skeleton3D bones, blend points): it goes entry by entry in numeric order,
/// each entry's keys in the declaring class's order, creating key first.
/// </summary>
internal sealed class PropertyOrder(GodotMetadata metadata)
{
    private const string MetadataPrefix = "metadata/";
    private const string ScriptProperty = "script";

    private readonly Dictionary<string, ClassOrder> tables = new(StringComparer.Ordinal);

    // The parts of Godot's list, in its order.
    private enum Tier
    {
        ClassList,
        Script,
        SlashNames,
        Exports,
        Metadata,
    }

    // Inside a class of the chain: its own list, then the theme overrides of Control or Window, then its dynamic names.
    private enum Family
    {
        OwnList,
        ThemeOverrides,
        Dynamic,
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

        if (!tables.TryGetValue(cls.Name, out ClassOrder? order))
        {
            order = Build(cls);
            tables.Add(cls.Name, order);
        }

        return [.. properties.OrderBy(property => RankOf(order, property.GodotName))];
    }

    // The exact name, then the chain's dynamic families base first (Godot lists the base class first), then the name's shape.
    private static Rank RankOf(ClassOrder order, string name)
    {
        if (order.Listed.TryGetValue(name, out Rank rank))
        {
            return rank;
        }

        foreach ((DynamicPropertyFamilies.DynamicFamily family, Rank slot) in order.Families)
        {
            Match match = family.Pattern.Match(name);
            if (match.Success)
            {
                return slot with { Entry = EntryOf(match), Key = KeyOf(family.KeyOrder, match) };
            }
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

    // The entry's number from the family's index group; 0 for a family without one, so its names keep source order. A
    // number past int's range goes last.
    private static int EntryOf(Match match)
    {
        Group index = match.Groups["index"];
        if (!index.Success)
        {
            return 0;
        }

        return int.TryParse(index.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int entry) ? entry : int.MaxValue;
    }

    // The key's place in the family's key order; a key the order lacks, or a family with no order, takes the order's length,
    // so it goes after the listed keys of its entry, in source order.
    private static int KeyOf(ImmutableArray<string> keyOrder, Match match)
    {
        Group key = match.Groups["key"];
        int place = key.Success ? keyOrder.IndexOf(key.Value) : -1;
        return place >= 0 ? place : keyOrder.Length;
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

    // Each chain class's dynamic families, base first, in the order the class adds them.
    private static ImmutableArray<(DynamicPropertyFamilies.DynamicFamily Family, Rank Slot)> FamiliesOf(ImmutableArray<GodotClassInfo> chain)
    {
        ImmutableArray<(DynamicPropertyFamilies.DynamicFamily Family, Rank Slot)>.Builder families = ImmutableArray.CreateBuilder<(
            DynamicPropertyFamilies.DynamicFamily Family,
            Rank Slot
        )>();
        for (int depth = 0; depth < chain.Length; depth++)
        {
            ImmutableArray<DynamicPropertyFamilies.DynamicFamily> declared = DynamicPropertyFamilies.Of(chain[depth].Name);
            for (int index = 0; index < declared.Length; index++)
            {
                families.Add((declared[index], new Rank(Tier.ClassList, depth, Family.Dynamic, index)));
            }
        }

        return families.ToImmutable();
    }

    private ClassOrder Build(GodotClassInfo cls)
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

        return new ClassOrder(listed, FamiliesOf(chain));
    }

    // A class's exact names and its chain's dynamic families.
    private sealed record ClassOrder(
        Dictionary<string, Rank> Listed,
        ImmutableArray<(DynamicPropertyFamilies.DynamicFamily Family, Rank Slot)> Families
    );

    // Entry and Key order a dynamic family's names: entry by entry in numeric order, each entry's keys in the class's order.
    private readonly record struct Rank(Tier Tier, int Depth = 0, Family Family = Family.OwnList, int Index = 0, int Entry = 0, int Key = 0)
        : IComparable<Rank>
    {
        public int CompareTo(Rank other) =>
            (Tier, Depth, Family, Index, Entry, Key).CompareTo((other.Tier, other.Depth, other.Family, other.Index, other.Entry, other.Key));
    }
}
