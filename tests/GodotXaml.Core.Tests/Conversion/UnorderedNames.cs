using System.Collections.Immutable;
using System.Text.RegularExpressions;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;

namespace GodotXaml.Core.Tests.Conversion;

// The names whose order the way back cannot hold, compared as a set: each run of them in one section is sorted on both sides.
// ResourceBackTests (from the alphabetical .xres text) and the ResourceConversionTests round trips both compare through it.
internal static partial class UnorderedNames
{
    private static readonly GodotMetadata Metadata = ResourceConversionTests.Metadata;

    // The names whose order the alphabetical .xres text cannot carry (docs/DECISIONS.md, "Canonical attribute order"),
    // compared as a set: each run of them in one section is sorted on both sides. Reviewed; add to it only with a reason.
    internal static readonly ImmutableArray<Entry> Entries =
    [
        new(
            "VisualShader varyings",
            // VisualShader::_get_property_list lists them in the order they were added, which the text keeps no record of.
            (section, name) => section.Class == "VisualShader" && name.StartsWith("varyings/", StringComparison.Ordinal),
            null
        ),
        new(
            "AnimationNodeBlendTree nodes",
            // AnimationNodeBlendTree::_get_property_list walks its nodes map (an AHashMap) in the order the nodes were added,
            // which the text keeps no record of (docs/DECISIONS.md, "Canonical attribute order").
            (section, name) => section.Class == "AnimationNodeBlendTree" && name.StartsWith("nodes/", StringComparison.Ordinal),
            null
        ),
        new(
            "TileSet sources",
            // TileSet::_get_property_list walks its sources (a HashMap, tile_set.h) in the order they were added, which the text
            // keeps no record of.
            (section, name) => section.Class == "TileSet" && name.StartsWith("sources/", StringComparison.Ordinal),
            null
        ),
        new(
            "TileSetAtlasSource tiles",
            // TileSetAtlasSource::_get_property_list walks its tiles and each tile's alternatives (HashMaps, tile_set.h) in the
            // order they were created, which the text keeps no record of; the way back puts tiles by coordinates and
            // alternatives by id. Only that order is hidden: whole tiles and whole alternatives move, and nothing else does.
            (section, name) => section.Class == "TileSetAtlasSource" && TileKey().IsMatch(name),
            ReorderTiles
        ),
        new(
            "FontFile support overrides",
            // FontFile::_get_property_list lists language_support_override/ and script_support_override/ from
            // get_language_support_overrides and get_script_support_overrides, which walk their HashMaps (font.cpp) in the order
            // the overrides were set, which the text keeps no record of.
            (section, name) =>
                section.Class == "FontFile"
                && (
                    name.StartsWith("language_support_override/", StringComparison.Ordinal)
                    || name.StartsWith("script_support_override/", StringComparison.Ordinal)
                ),
            null
        ),
        new(
            "BoneMap bone map",
            // BoneMap::_get_property_list walks its bone_map (a HashMap, bone_map.cpp) in the order the profile's bones were
            // added, which the text keeps no record of.
            (section, name) => section.Class == "BoneMap" && name.StartsWith("bone_map/", StringComparison.Ordinal),
            null
        ),
        new(
            "quoted_name script exports",
            // A script's own _get_property_list order; neither the class metadata nor a family row lists these names.
            (section, name) => section.Fixture == "quoted_name" && section.Root && IsScriptExport(section.Class, name),
            null
        ),
    ];

    /// <summary>
    /// The document with each run of unordered names reordered as its entry says (sorted ordinally by name for an entry with
    /// no reorder of its own); every other property keeps its place.
    /// </summary>
    internal static TscnDocument Sort(string fixture, TscnDocument document) =>
        document with
        {
            ResourceProperties = SortRuns(new Section(fixture, document.Header.Type, true), document.ResourceProperties),
            SubResources =
            [
                .. document.SubResources.Select(sub => sub with { Properties = SortRuns(new Section(fixture, sub.Type, false), sub.Properties) }),
            ],
        };

    /// <summary>
    /// Godot's text as the round trips compare it: unchanged when reordering leaves each unordered run as it is, else written
    /// again from the reordered document.
    /// </summary>
    internal static string Text(string fixture, string text)
    {
        TscnDocument document = TscnReader.Read(text);
        bool sorted = Sections(fixture, document)
            .All(pair => Runs(pair.Properties, name => IsUnordered(pair.Section, name)).All(run => Reordered(pair.Section, run).SequenceEqual(run)));
        return sorted ? text : TscnWriter.Write(Sort(fixture, document));
    }

    /// <summary>The runs of consecutive properties whose names match, in section order.</summary>
    internal static IEnumerable<List<TscnProperty>> Runs(ImmutableArray<TscnProperty> properties, Func<string, bool> matches)
    {
        List<TscnProperty> run = [];
        foreach (TscnProperty property in properties)
        {
            if (matches(property.Name))
            {
                run.Add(property);
            }
            else if (run.Count > 0)
            {
                yield return run;
                run = [];
            }
        }

        if (run.Count > 0)
        {
            yield return run;
        }
    }

    /// <summary>Each section of a fixture with its properties: the [resource] section, then the sub-resources.</summary>
    internal static IEnumerable<(Section Section, ImmutableArray<TscnProperty> Properties)> Sections(string fixture, TscnDocument document) =>
        [
            (new Section(fixture, document.Header.Type, true), document.ResourceProperties),
            .. document.SubResources.Select(sub => (new Section(fixture, sub.Type, false), sub.Properties)),
        ];

    private static bool IsUnordered(Section section, string name) => Entries.Any(entry => entry.Matches(section, name));

    // A run as the round trips compare it: its first name's entry's reorder, else sorted ordinally by name.
    private static List<TscnProperty> Reordered(Section section, List<TscnProperty> run) =>
        Entries.FirstOrDefault(entry => entry.Matches(section, run[0].Name)) is { Reorder: { } reorder }
            ? reorder(run)
            : [.. run.OrderBy(property => property.Name, StringComparer.Ordinal)];

    // Tile order only: the run's blocks of one tile each, ordered by tile, each with its alternatives reordered. A tile written
    // in two separate blocks (its names interleaved with another tile's) leaves the run as it is, so the interleaving shows.
    private static List<TscnProperty> ReorderTiles(List<TscnProperty> run)
    {
        List<List<TscnProperty>> tiles = Blocks(run, TileOf);
        return IsRepeated(tiles, TileOf)
            ? run
            : [.. tiles.Select(ReorderAlternatives).OrderBy(tile => TileOf(tile[0]), StringComparer.Ordinal).SelectMany(tile => tile)];
    }

    // Alternative order only: a tile's alternative blocks (each its marker and its keys) by id, in the places the alternatives
    // held, so the tile's own keys keep theirs; an alternative or the own keys in two separate blocks leave the tile as it is.
    private static List<TscnProperty> ReorderAlternatives(List<TscnProperty> tile)
    {
        List<List<TscnProperty>> blocks = Blocks(tile, AlternativeOf);
        if (IsRepeated(blocks, AlternativeOf))
        {
            return tile;
        }

        Queue<List<TscnProperty>> alternatives = new(
            blocks.Where(block => AlternativeOf(block[0]).Length > 0).OrderBy(block => AlternativeOf(block[0]), StringComparer.Ordinal)
        );
        return [.. blocks.SelectMany(block => AlternativeOf(block[0]).Length > 0 ? alternatives.Dequeue() : block)];
    }

    // The maximal runs of consecutive properties with one key.
    private static List<List<TscnProperty>> Blocks(List<TscnProperty> properties, Func<TscnProperty, string> keyOf)
    {
        List<List<TscnProperty>> blocks = [];
        foreach (TscnProperty property in properties)
        {
            if (blocks.Count > 0 && keyOf(blocks[^1][0]) == keyOf(property))
            {
                blocks[^1].Add(property);
            }
            else
            {
                blocks.Add([property]);
            }
        }

        return blocks;
    }

    // Whether two separate blocks share a key.
    private static bool IsRepeated(List<List<TscnProperty>> blocks, Func<TscnProperty, string> keyOf) =>
        blocks.Select(block => keyOf(block[0])).Distinct(StringComparer.Ordinal).Count() < blocks.Count;

    // An atlas name's tile.
    private static string TileOf(TscnProperty property) => TileGroupPattern().Match(property.Name).Groups["tile"].Value;

    // An atlas name's alternative id, or empty for a tile's own key.
    private static string AlternativeOf(TscnProperty property) => TileGroupPattern().Match(property.Name).Groups["alternative"].Value;

    // Each run of consecutive unordered names is reordered; every other property keeps its place.
    private static ImmutableArray<TscnProperty> SortRuns(Section section, ImmutableArray<TscnProperty> properties)
    {
        List<TscnProperty> result = [];
        int next = 0;
        foreach (List<TscnProperty> run in Runs(properties, name => IsUnordered(section, name)))
        {
            int start = properties.IndexOf(run[0], next);
            result.AddRange(properties[next..start]);
            result.AddRange(Reordered(section, run));
            next = start + run.Count;
        }

        result.AddRange(properties[next..]);
        return [.. result];
    }

    // A name the class neither declares nor gives through a family row, nor the script or metadata line.
    private static bool IsScriptExport(string? className, string name)
    {
        GodotClassInfo? cls = className is null ? null : Metadata.FindClass(className);
        return cls is not null
            && name != "script"
            && !name.StartsWith("metadata/", StringComparison.Ordinal)
            && Metadata.FindProperty(cls, name) is null
            && !DynamicPropertyFamilies.Of(cls.Name).Any(family => family.Pattern.IsMatch(name));
    }

    // A TileSetAtlasSource tile's name: <x>:<y>/ and any key.
    [GeneratedRegex(@"^[0-9]+:[0-9]+/")]
    private static partial Regex TileKey();

    // A TileSetAtlasSource tile's name: its tile, and its alternative's id when it names one.
    [GeneratedRegex(@"^(?<tile>[0-9]+:[0-9]+)/(?:(?<alternative>[0-9]+)(?:/|\z))?")]
    private static partial Regex TileGroupPattern();

    /// <summary>One section of a fixture: its class, and whether it is the [resource] section.</summary>
    internal sealed record Section(string Fixture, string? Class, bool Root);

    /// <summary>
    /// A reviewed entry: its name, which names of a section it covers, and how its runs are reordered when only part of their
    /// order is unknown (null: sorted ordinally by name).
    /// </summary>
    internal sealed record Entry(string Name, Func<Section, string, bool> Matches, Func<List<TscnProperty>, List<TscnProperty>>? Reorder);
}
