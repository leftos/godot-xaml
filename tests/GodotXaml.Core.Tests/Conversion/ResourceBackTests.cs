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

    private static string Comparable(string fixture, TscnDocument document) => TscnTestNormalizer.Comparable(UnorderedNames.Sort(fixture, document));
}
