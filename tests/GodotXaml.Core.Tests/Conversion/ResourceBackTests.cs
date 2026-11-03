using System.Collections.Immutable;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// .xres goldens read from disk to TscnDocument, written with TscnWriter and compared with what Godot 4.7.2 wrote. The
// .xres text is written in sorted name order, so only the way back restores Godot's property order.
public sealed class ResourceBackTests
{
    private static readonly GodotMetadata Metadata = ResourceConversionTests.Metadata;

    // The names whose order the alphabetical .xres text cannot carry (docs/DECISIONS.md, "Canonical attribute order"),
    // compared as a set: each run of them in one section is sorted on both sides. Reviewed; add to it only with a reason.
    private static readonly ImmutableArray<Unordered> UnorderedNames =
    [
        new(
            "VisualShader varyings",
            // VisualShader::_get_property_list lists them in the order they were added, which the text keeps no record of.
            (section, name) => section.Class == "VisualShader" && name.StartsWith("varyings/", StringComparison.Ordinal)
        ),
        new(
            "quoted_name script exports",
            // A script's own _get_property_list order; neither the class metadata nor a family row lists these names.
            (section, name) => section.Fixture == "quoted_name" && section.Root && IsScriptExport(section.Class, name)
        ),
    ];

    public static TheoryData<string> Fixtures => ResourceForwardTests.Resources;

    public static TheoryData<string> UnorderedEntries => new(UnorderedNames.Select(entry => entry.Name));

    // Ids normalised on both sides; property order is compared as Godot wrote it, but for the unordered names.
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ConvertsXresToTheGodotResource(string fixture)
    {
        TscnDocument expected = TscnReader.Read(ResourceForwardTests.ReadFixture("scenes", fixture + ".tres"));
        XscnParseResult parsed = XscnParser.Parse(
            ResourceForwardTests.ReadFixture("converted", fixture + ".xres"),
            XscnDocumentKind.Resource,
            Metadata
        );
        Assert.Empty(parsed.Diagnostics);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, Metadata, ResourceConversionTests.Options);

        Assert.Empty(back.Issues);
        Assert.Equal(Comparable(fixture, expected), Comparable(fixture, back.Document));
    }

    // An entry is stale when no fixture holds a run of its names that Godot wrote out of ordinal order: sorting then
    // hides nothing.
    [Theory]
    [MemberData(nameof(UnorderedEntries))]
    public void EachUnorderedEntryHidesAnOutOfOrderRun(string entry)
    {
        Unordered unordered = Assert.Single(UnorderedNames, candidate => candidate.Name == entry);

        List<List<string>> runs =
        [
            .. ResourceForwardTests
                .Resources.Select(row => row.Data)
                .SelectMany(fixture =>
                    Sections(fixture, TscnReader.Read(ResourceForwardTests.ReadFixture("scenes", fixture + ".tres")))
                        .SelectMany(pair => Runs(pair.Properties, name => unordered.Matches(pair.Section, name)))
                        .Select(run => run.Select(property => property.Name).ToList())
                ),
        ];

        Assert.NotEmpty(runs);
        Assert.Contains(runs, run => !run.SequenceEqual(run.Order(StringComparer.Ordinal)));
    }

    private static string Comparable(string fixture, TscnDocument document) =>
        TscnTestNormalizer.Comparable(
            document with
            {
                ResourceProperties = SortUnordered(new Section(fixture, document.Header.Type, true), document.ResourceProperties),
                SubResources =
                [
                    .. document.SubResources.Select(sub =>
                        sub with
                        {
                            Properties = SortUnordered(new Section(fixture, sub.Type, false), sub.Properties),
                        }
                    ),
                ],
            }
        );

    // Each run of consecutive unordered names is sorted ordinally; every other property keeps its place.
    private static ImmutableArray<TscnProperty> SortUnordered(Section section, ImmutableArray<TscnProperty> properties)
    {
        bool IsUnordered(string name) => UnorderedNames.Any(entry => entry.Matches(section, name));
        List<TscnProperty> result = [];
        int next = 0;
        foreach (List<TscnProperty> run in Runs(properties, IsUnordered))
        {
            int start = properties.IndexOf(run[0], next);
            result.AddRange(properties[next..start]);
            result.AddRange(run.OrderBy(item => item.Name, StringComparer.Ordinal));
            next = start + run.Count;
        }

        result.AddRange(properties[next..]);
        return [.. result];
    }

    // The runs of consecutive properties whose names match, in section order.
    private static IEnumerable<List<TscnProperty>> Runs(ImmutableArray<TscnProperty> properties, Func<string, bool> matches)
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

    private static IEnumerable<(Section Section, ImmutableArray<TscnProperty> Properties)> Sections(string fixture, TscnDocument document) =>
        [
            (new Section(fixture, document.Header.Type, true), document.ResourceProperties),
            .. document.SubResources.Select(sub => (new Section(fixture, sub.Type, false), sub.Properties)),
        ];

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

    // One section of a fixture: its class, and whether it is the [resource] section.
    private sealed record Section(string Fixture, string? Class, bool Root);

    // A reviewed entry: its name, and which names of a section it covers.
    private sealed record Unordered(string Name, Func<Section, string, bool> Matches);
}
