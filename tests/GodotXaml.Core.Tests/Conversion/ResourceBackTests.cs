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

    public static TheoryData<string> Fixtures => ResourceForwardTests.Resources;

    public static TheoryData<string> UnorderedEntries => new(UnorderedNames.Entries.Select(entry => entry.Name));

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
        UnorderedNames.Entry unordered = Assert.Single(UnorderedNames.Entries, candidate => candidate.Name == entry);

        List<List<string>> runs =
        [
            .. ResourceForwardTests
                .Resources.Select(row => row.Data)
                .SelectMany(fixture =>
                    UnorderedNames
                        .Sections(fixture, TscnReader.Read(ResourceForwardTests.ReadFixture("scenes", fixture + ".tres")))
                        .SelectMany(pair => UnorderedNames.Runs(pair.Properties, name => unordered.Matches(pair.Section, name)))
                        .Select(run => run.Select(property => property.Name).ToList())
                ),
        ];

        Assert.NotEmpty(runs);
        Assert.Contains(runs, run => !run.SequenceEqual(run.Order(StringComparer.Ordinal)));
    }

    // The atlas entry hides tile and alternative order only: whole tiles and whole alternatives move, nothing else.
    [Fact]
    public void TheAtlasEntryHidesTileAndAlternativeOrderOnly()
    {
        Assert.Equal(Atlas("1:0/0", "0:0/1", "0:0/0", "0:0/0/flip_h"), Atlas("0:0/0", "0:0/0/flip_h", "0:0/1", "1:0/0"));
        Assert.NotEqual(Atlas("0:0/0", "1:0/0", "0:0/0/flip_h"), Atlas("0:0/0", "0:0/0/flip_h", "1:0/0"));
        Assert.NotEqual(Atlas("0:0/0", "0:0/size_in_atlas"), Atlas("0:0/size_in_atlas", "0:0/0"));
    }

    // A TileSetAtlasSource resource holding the names in this order, as the round trips compare it.
    private static string Atlas(params string[] names) =>
        Comparable(
            "atlas",
            TscnReader.Read(
                "[gd_resource type=\"TileSetAtlasSource\" format=3]\n\n[resource]\n" + string.Concat(names.Select(name => $"{name} = 0\n"))
            )
        );

    private static string Comparable(string fixture, TscnDocument document) => TscnTestNormalizer.Comparable(UnorderedNames.Sort(fixture, document));
}
