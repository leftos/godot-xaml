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

    // The FontFile entry hides size, glyph and kerning pair order only: whole sizes, whole glyphs and whole pairs move, and
    // textures, a glyph's keys and a glyph split around another stay as they are.
    [Fact]
    public void TheFontFileEntryHidesSizeGlyphAndKerningOrderOnly()
    {
        Assert.Equal(
            Font("cache/0/9/0/ascent", "cache/0/9/0/scale", "cache/0/8/0/ascent", "cache/0/8/0/scale"),
            Font("cache/0/8/0/ascent", "cache/0/8/0/scale", "cache/0/9/0/ascent", "cache/0/9/0/scale")
        );
        Assert.Equal(
            Font("cache/0/9/0/glyphs/66/offset", "cache/0/9/0/glyphs/66/size", "cache/0/9/0/glyphs/3/offset", "cache/0/9/0/glyphs/3/size"),
            Font("cache/0/9/0/glyphs/3/offset", "cache/0/9/0/glyphs/3/size", "cache/0/9/0/glyphs/66/offset", "cache/0/9/0/glyphs/66/size")
        );
        Assert.Equal(
            Font("cache/0/9/0/glyphs/3/offset", "cache/0/9/0/kerning_overrides/65/66", "cache/0/9/0/kerning_overrides/3/12"),
            Font("cache/0/9/0/glyphs/3/offset", "cache/0/9/0/kerning_overrides/3/12", "cache/0/9/0/kerning_overrides/65/66")
        );
        Assert.NotEqual(
            Font("cache/0/9/0/textures/10/offsets", "cache/0/9/0/textures/2/offsets"),
            Font("cache/0/9/0/textures/2/offsets", "cache/0/9/0/textures/10/offsets")
        );
        Assert.NotEqual(
            Font("cache/0/9/0/glyphs/65/offset", "cache/0/9/0/glyphs/65/advance"),
            Font("cache/0/9/0/glyphs/65/advance", "cache/0/9/0/glyphs/65/offset")
        );
        Assert.NotEqual(
            Font("cache/0/9/0/glyphs/65/advance", "cache/0/9/0/glyphs/66/advance", "cache/0/9/0/glyphs/65/offset"),
            Font("cache/0/9/0/glyphs/65/advance", "cache/0/9/0/glyphs/65/offset", "cache/0/9/0/glyphs/66/advance")
        );
    }

    // A FontFile resource holding the names in this order, as the round trips compare it.
    private static string Font(params string[] names) =>
        Comparable(
            "font",
            TscnReader.Read("[gd_resource type=\"FontFile\" format=3]\n\n[resource]\n" + string.Concat(names.Select(name => $"{name} = 0\n")))
        );

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
