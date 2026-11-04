using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Values;
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
/// the order among script exports or inside a dynamic family, so those keep source order: a family whose names Godot sets
/// in any order, or whose entries a count property ranked before it sizes first, loads that way. A family whose entries
/// Godot creates on one key goes entry by entry in numeric order, each entry's keys in the declaring class's order,
/// creating key first; a sized family whose keys reset one another does the same. A family can order one nested level the
/// same way: inside a key, its sub-entries in numeric order, each sub-entry's keys in the declaring class's order. A family
/// whose key order depends on a sibling property (a VisualShader's flags/, listed in the order its mode registers them)
/// reads that property from the section once per sort: the last one holding an integer, else 0; a value the family has no
/// order for keeps source order. A family whose order runs deeper than that (a TileSetAtlasSource's tiles, alternatives and
/// TileData keys, an AudioBusLayout's buses and effects) is placed by its row's ranker, which reads the class list of the
/// class the row names when it names one; the metadata must hold that class only then. A family Godot lists sorted by name
/// (an instance's shader parameters) goes by code point among the section's names of it, and a family Godot lists in
/// another resource's order (a ShaderMaterial's parameters, a MeshInstance3D's blend shapes) goes in the list the
/// conversion's <see cref="SourceOrders"/> reads from its sibling's resource, keeping source order when there is none.
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
    /// <param name="sources">The conversion's lists a by-source family is ordered by.</param>
    /// <returns>The sorted properties.</returns>
    public ImmutableArray<XscnProperty> Sort(GodotClassInfo? cls, ImmutableArray<XscnProperty> properties, SourceOrders sources)
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

        RankContext context = new(properties, sources);
        return [.. properties.OrderBy(property => RankOf(order, property.GodotName, context))];
    }

    // The exact name, then the chain's dynamic families base first (Godot lists the base class first), then the name's shape.
    private static Rank RankOf(ClassOrder order, string name, RankContext context)
    {
        if (order.Listed.TryGetValue(name, out Rank rank))
        {
            return rank;
        }

        foreach ((DynamicPropertyFamilies.DynamicFamily family, Rank slot, ImmutableArray<(string Key, int Index)> classKeys) in order.Families)
        {
            if (RankIn(family, slot, classKeys, name, context) is { } matched)
            {
                return matched;
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

    // The family's rank for a name, or null when the family does not name it. A family the pattern alone names by one literal
    // matches by an ordinal comparison, which costs less than its regex. A ranked family's ranker places the name, from the
    // match and its row's class list. A by-name family's literal prefix names it, and its place among the section's names
    // of the family places it. A by-source family's literal prefix names it too, and the place of the rest of the name in
    // the list its sibling's resource holds places it.
    private static Rank? RankIn(
        DynamicPropertyFamilies.DynamicFamily family,
        Rank slot,
        ImmutableArray<(string Key, int Index)> classKeys,
        string name,
        RankContext context
    ) =>
        family.Creation switch
        {
            DynamicPropertyFamilies.EntryCreation.ByName => RankByName(family, slot, name, context),
            DynamicPropertyFamilies.EntryCreation.BySource => RankBySource(family, slot, name, context),
            _ => RankByPattern(family, slot, classKeys, name, context),
        };

    private static Rank? RankByName(DynamicPropertyFamilies.DynamicFamily family, Rank slot, string name, RankContext context) =>
        family.Literal is { } prefix && MatchesLiteral(prefix, name) ? slot with { Entry = context.NamePlaceOf(prefix, name) } : null;

    private static Rank? RankBySource(DynamicPropertyFamilies.DynamicFamily family, Rank slot, string name, RankContext context) =>
        family is { Literal: { } prefix, SourceBy: { } sourceBy, SourceRead: { } read } && MatchesLiteral(prefix, name)
            ? slot with
            {
                Entry = context.SourcePlaceOf(sourceBy, read, name[prefix.Text.Length..]),
            }
            : null;

    private static Rank? RankByPattern(
        DynamicPropertyFamilies.DynamicFamily family,
        Rank slot,
        ImmutableArray<(string Key, int Index)> classKeys,
        string name,
        RankContext context
    )
    {
        if (family.Literal is { } literal)
        {
            return MatchesLiteral(literal, name) ? slot : null;
        }

        Match match = family.Pattern.Match(name);
        if (match.Success && family.Ranker is { } ranker)
        {
            DynamicPropertyFamilies.RankLevels levels = ranker(match, classKeys);
            return slot with
            {
                Entry = levels.Entry,
                Key = levels.Key,
                SubEntry = levels.SubEntry,
                SubKey = levels.SubKey,
                Layer = levels.Layer,
                Leaf = levels.Leaf,
            };
        }

        return match.Success
            ? slot with
            {
                Entry = EntryOf(match, "index"),
                Key = KeyOf(KeyLookupOf(family, context), match, "key"),
                SubEntry = EntryOf(match, "subindex"),
                SubKey = SubKeyOf(family, match),
            }
            : null;
    }

    // Whether a family's literal names a property: its prefix, or exactly the whole name.
    internal static bool MatchesLiteral((string Text, DynamicPropertyFamilies.LiteralMatch Kind) literal, string name) =>
        literal.Kind == DynamicPropertyFamilies.LiteralMatch.Exact
            ? name.Equals(literal.Text, StringComparison.Ordinal)
            : name.StartsWith(literal.Text, StringComparison.Ordinal);

    // The entry's number from the named index group (index, or subindex for a nested entry); 0 for a family without one, so
    // its names keep source order. A number past int's range goes last.
    private static int EntryOf(Match match, string group)
    {
        Group index = match.Groups[group];
        if (!index.Success)
        {
            return 0;
        }

        return int.TryParse(index.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int entry) ? entry : int.MaxValue;
    }

    // The family's key lookup; for a family whose order a sibling property chooses, the order for the section's value of it,
    // or an empty one for a value the family has none for, which keeps every key in source order.
    private static ImmutableArray<(string Key, int Index)> KeyLookupOf(DynamicPropertyFamilies.DynamicFamily family, RankContext context)
    {
        if (family.ModeBy is not { } modeBy)
        {
            return family.KeyLookup;
        }

        long mode = context.ModeOf(modeBy);
        return mode >= 0 && mode < family.ModeKeyLookups.Length ? family.ModeKeyLookups[(int)mode] : [];
    }

    // The place in the key order of the named key group (key, or subkey for a nested entry); a key the order lacks, or a
    // family with no order, takes the order's length, so it goes after the listed keys of its entry, in source order.
    private static int KeyOf(ImmutableArray<(string Key, int Index)> keyOrder, Match match, string group)
    {
        Group key = match.Groups[group];
        return key.Success ? PlaceOf(keyOrder, key.ValueSpan) : keyOrder.Length;
    }

    // The place a key holds in an order sorted by key, or the order's length for one it does not hold; the probe compares the
    // group's span, so it allocates nothing.
    internal static int PlaceOf(ImmutableArray<(string Key, int Index)> order, ReadOnlySpan<char> key)
    {
        int low = 0;
        int high = order.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int comparison = key.SequenceCompareTo(order[middle].Key);
            switch (comparison)
            {
                case 0:
                    return order[middle].Index;
                case < 0:
                    high = middle - 1;
                    break;
                default:
                    low = middle + 1;
                    break;
            }
        }

        return order.Length;
    }

    // A family with no nested key order has no keys below its entries' keys; otherwise the sub-key's place.
    private static int SubKeyOf(DynamicPropertyFamilies.DynamicFamily family, Match match) =>
        family.SubKeyOrder.IsEmpty ? 0 : KeyOf(family.SubKeyLookup, match, "subkey");

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

    // Each chain class's dynamic families, base first, in the order the class adds them, each with the class list its ranker
    // places keys by (empty for a family without one).
    private ImmutableArray<(DynamicPropertyFamilies.DynamicFamily Family, Rank Slot, ImmutableArray<(string Key, int Index)> ClassKeys)> FamiliesOf(
        ImmutableArray<GodotClassInfo> chain
    )
    {
        ImmutableArray<(
            DynamicPropertyFamilies.DynamicFamily Family,
            Rank Slot,
            ImmutableArray<(string Key, int Index)> ClassKeys
        )>.Builder families = ImmutableArray.CreateBuilder<(
            DynamicPropertyFamilies.DynamicFamily Family,
            Rank Slot,
            ImmutableArray<(string Key, int Index)> ClassKeys
        )>();
        for (int depth = 0; depth < chain.Length; depth++)
        {
            ImmutableArray<DynamicPropertyFamilies.DynamicFamily> declared = DynamicPropertyFamilies.Of(chain[depth].Name);
            for (int index = 0; index < declared.Length; index++)
            {
                families.Add(
                    (declared[index], new Rank(Tier.ClassList, depth, Family.Dynamic, index), ClassKeysOf(chain[depth].Name, declared[index]))
                );
            }
        }

        return families.ToImmutable();
    }

    // The class list a ranked family places keys by, from the metadata in use; empty for a family whose ranker reads none
    // (ListedBy null). Only a named class is required: a metadata without it cannot place the family's keys, which is an
    // error in the metadata, not an order.
    private ImmutableArray<(string Key, int Index)> ClassKeysOf(string declaring, DynamicPropertyFamilies.DynamicFamily family)
    {
        if (family.ListedBy is not { } listedBy)
        {
            return [];
        }

        GodotClassInfo listed =
            metadata.FindClass(listedBy)
            ?? throw new InvalidDataException(
                $"The metadata has no class {listedBy}, whose list the {declaring} family {family.Pattern} places keys by."
            );
        return DynamicPropertyFamilies.KeyLookup(listed.ListOrder);
    }

    private ClassOrder Build(GodotClassInfo cls)
    {
        ImmutableArray<GodotClassInfo> chain = [.. metadata.ChainOf(cls).Reverse()];
        Dictionary<string, Rank> listed = new(StringComparer.Ordinal);
        for (int depth = 0; depth < chain.Length; depth++)
        {
            // ListOrder, not Properties: it holds the slash-named names ClassDB lists too (glow_levels/1, params/bias).
            ImmutableArray<string> own = chain[depth].ListOrder;
            for (int index = 0; index < own.Length; index++)
            {
                listed.TryAdd(own[index], new Rank(Tier.ClassList, depth, Family.OwnList, index));
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
        ImmutableArray<(DynamicPropertyFamilies.DynamicFamily Family, Rank Slot, ImmutableArray<(string Key, int Index)> ClassKeys)> Families
    );

    // The section one sort ranks, for a family whose key order a sibling property chooses, a family sorted by name and a
    // family ordered by a list its sibling's resource holds; each is read on first need only, so a section whose names no
    // such family places never reads it.
    private sealed class RankContext(ImmutableArray<XscnProperty> properties, SourceOrders sources)
    {
        private string? modeBy;
        private long mode;
        private Dictionary<string, Dictionary<string, int>>? namePlaces;
        private string? sourceBy;
        private SourceList? sourceList;

        // The name's place in the list the last property of that name gives, since each set replaces the last; 0 for every
        // name when there is no such property or it gives no list, which keeps them in source order.
        public int SourcePlaceOf(string property, DynamicPropertyFamilies.SourceRead read, string name)
        {
            if (!string.Equals(sourceBy, property, StringComparison.Ordinal))
            {
                sourceList = LastValue(property) is { } value ? sources.ListOf(value, read) : null;
                sourceBy = property;
            }

            return sourceList?.PlaceOf(name) ?? 0;
        }

        private XscnValue? LastValue(string property)
        {
            for (int index = properties.Length - 1; index >= 0; index--)
            {
                if (properties[index].GodotName == property)
                {
                    return properties[index].Value;
                }
            }

            return null;
        }

        // The name's place among the section's names the literal prefix names, sorted by code point as StringName::AlphCompare
        // (str_compare) sorts them; a repeated name holds one place, so the stable sort keeps its copies in source order.
        public int NamePlaceOf((string Text, DynamicPropertyFamilies.LiteralMatch Kind) prefix, string name)
        {
            namePlaces ??= new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            if (!namePlaces.TryGetValue(prefix.Text, out Dictionary<string, int>? places))
            {
                places = PlacesByName(prefix);
                namePlaces.Add(prefix.Text, places);
            }

            return places[name];
        }

        private Dictionary<string, int> PlacesByName((string Text, DynamicPropertyFamilies.LiteralMatch Kind) prefix)
        {
            List<string> names =
            [
                .. properties
                    .Select(property => property.GodotName)
                    .Where(name => MatchesLiteral(prefix, name))
                    .Distinct(StringComparer.Ordinal)
                    .Order(Comparer<string>.Create(GodotKeyOrder.CompareCodePoints)),
            ];
            Dictionary<string, int> places = new(names.Count, StringComparer.Ordinal);
            for (int index = 0; index < names.Count; index++)
            {
                places.Add(names[index], index);
            }

            return places;
        }

        // The integer value of the last property of that name holding one, since each set replaces the last (a VisualShader's
        // set_mode also clears its flags); else 0 (a VisualShader with no mode is Spatial).
        public long ModeOf(string property)
        {
            if (!string.Equals(modeBy, property, StringComparison.Ordinal))
            {
                mode = Read(property);
                modeBy = property;
            }

            return mode;
        }

        private long Read(string property)
        {
            for (int index = properties.Length - 1; index >= 0; index--)
            {
                XscnProperty candidate = properties[index];
                if (candidate.GodotName == property && candidate.Value is XscnScalar { Value: GodotInt { Value: long value } })
                {
                    return value;
                }
            }

            return 0;
        }
    }

    // Entry and Key order a dynamic family's names: entry by entry in numeric order, each entry's keys in the class's order.
    // SubEntry and SubKey order one nested level inside a key the same way (a setting's joints, each joint's keys). Layer and
    // Leaf order two levels deeper, which only a ranked family sets (an atlas alternative's TileData keys); 0 for any other.
    private readonly record struct Rank(
        Tier Tier,
        int Depth = 0,
        Family Family = Family.OwnList,
        int Index = 0,
        int Entry = 0,
        int Key = 0,
        int SubEntry = 0,
        int SubKey = 0,
        int Layer = 0,
        int Leaf = 0
    ) : IComparable<Rank>
    {
        public int CompareTo(Rank other) =>
            (Tier, Depth, Family, Index, Entry, Key, SubEntry, SubKey, Layer, Leaf).CompareTo(
                (other.Tier, other.Depth, other.Family, other.Index, other.Entry, other.Key, other.SubEntry, other.SubKey, other.Layer, other.Leaf)
            );
    }
}
