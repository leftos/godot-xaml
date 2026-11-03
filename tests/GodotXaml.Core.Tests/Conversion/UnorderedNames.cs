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
            (section, name) => section.Class == "VisualShader" && name.StartsWith("varyings/", StringComparison.Ordinal)
        ),
        new(
            "AnimationNodeBlendTree nodes",
            // AnimationNodeBlendTree::_get_property_list walks its nodes map (an AHashMap) in the order the nodes were added,
            // which the text keeps no record of (docs/DECISIONS.md, "Canonical attribute order").
            (section, name) => section.Class == "AnimationNodeBlendTree" && name.StartsWith("nodes/", StringComparison.Ordinal)
        ),
        new(
            "TileSet sources",
            // TileSet::_get_property_list walks its sources (a HashMap, tile_set.h) in the order they were added, which the text
            // keeps no record of.
            (section, name) => section.Class == "TileSet" && name.StartsWith("sources/", StringComparison.Ordinal)
        ),
        new(
            "TileSetAtlasSource tiles",
            // TileSetAtlasSource::_get_property_list walks its tiles and each tile's alternatives (HashMaps, tile_set.h) in the
            // order they were created, which the text keeps no record of; and the way back sets each terrain key after every
            // other tile key, since TileData::set_terrain and set_terrain_peering_bit fail while terrain_set is unset.
            (section, name) => section.Class == "TileSetAtlasSource" && TileKey().IsMatch(name)
        ),
        new(
            "quoted_name script exports",
            // A script's own _get_property_list order; neither the class metadata nor a family row lists these names.
            (section, name) => section.Fixture == "quoted_name" && section.Root && IsScriptExport(section.Class, name)
        ),
    ];

    /// <summary>The document with each run of unordered names sorted ordinally; every other property keeps its place.</summary>
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
    /// Godot's text as the round trips compare it: unchanged when it holds no unordered run out of ordinal order, else written
    /// again from the sorted document.
    /// </summary>
    internal static string Text(string fixture, string text)
    {
        TscnDocument document = TscnReader.Read(text);
        bool sorted = Sections(fixture, document)
            .SelectMany(pair => Runs(pair.Properties, name => IsUnordered(pair.Section, name)))
            .All(run => run.Select(property => property.Name).SequenceEqual(run.Select(property => property.Name).Order(StringComparer.Ordinal)));
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

    // Each run of consecutive unordered names is sorted ordinally; every other property keeps its place.
    private static ImmutableArray<TscnProperty> SortRuns(Section section, ImmutableArray<TscnProperty> properties)
    {
        List<TscnProperty> result = [];
        int next = 0;
        foreach (List<TscnProperty> run in Runs(properties, name => IsUnordered(section, name)))
        {
            int start = properties.IndexOf(run[0], next);
            result.AddRange(properties[next..start]);
            result.AddRange(run.OrderBy(item => item.Name, StringComparer.Ordinal));
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
    [GeneratedRegex(@"^\d+:\d+/")]
    private static partial Regex TileKey();

    /// <summary>One section of a fixture: its class, and whether it is the [resource] section.</summary>
    internal sealed record Section(string Fixture, string? Class, bool Root);

    /// <summary>A reviewed entry: its name, and which names of a section it covers.</summary>
    internal sealed record Entry(string Name, Func<Section, string, bool> Matches);
}
