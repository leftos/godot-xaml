using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// .tres to XscnDocument and back to TscnDocument, written with TscnWriter and compared with what Godot 4.7.2 wrote.
public sealed partial class ResourceConversionTests
{
    internal static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    // The options the resource tests convert with, both ways.
    internal static readonly ConversionOptions Options = new()
    {
        Seed = 7,
        ScriptClassOf = path => path == "res://tagged.gd" ? "TaggedResource" : null,
    };

    public static TheoryData<string> Fixtures =>
        new(
            "material.tres",
            "gradient_texture.tres",
            "quoted_name.tres",
            "script_class.tres",
            "packed_bytes.tres",
            "animation_tracks.tres",
            "visual_shader_nodes.tres",
            "replication_properties.tres",
            "tile_set_patterns.tres",
            "visual_shader_types.tres",
            "animation_nodes.tres",
            "skeleton_modification_singles.tres"
        );

    // With the forward pass's ids kept, the way back writes the bytes Godot wrote.
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ConvertsGodotResourceThereAndBackByteIdentical(string file)
    {
        string text = ReadFixture(file);
        ConversionResult<XscnDocument> forward = TscnToXscn.Convert(TscnReader.Read(text), Metadata, Options);
        Assert.Empty(forward.Issues);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(forward.Document, Metadata, Options with { KeepIds = forward.Ids });

        Assert.Empty(back.Issues);
        Assert.Equal(text, TscnWriter.Write(back.Document));
    }

    // With ids minted again, the way back differs from Godot's file only in the ids.
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ConvertsGodotResourceThereAndBackWithNewIds(string file)
    {
        string text = ReadFixture(file);
        ConversionResult<XscnDocument> forward = TscnToXscn.Convert(TscnReader.Read(text), Metadata, Options);
        Assert.Empty(forward.Issues);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(forward.Document, Metadata, Options);

        Assert.Empty(back.Issues);
        Assert.Equal(Renumber(text), Renumber(TscnWriter.Write(back.Document)));
    }

    [Fact]
    public void SubResourceUsedTwiceIsOneKeyedEntry()
    {
        const string text = """
            [gd_resource type="Resource" format=3]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_a1b2c"]
            bg_color = Color(1, 0, 0, 1)

            [resource]
            metadata/first = SubResource("StyleBoxFlat_a1b2c")
            metadata/second = SubResource("StyleBoxFlat_a1b2c")

            """;

        XscnDocument document = ForwardAndBack(text);

        XscnResource entry = Assert.Single(document.Resources);
        Assert.Equal("StyleBoxFlat1", entry.Key);
        Assert.Equal("StyleBoxFlat", Assert.IsType<XscnClassType>(entry.Type).Class.Name);
        Assert.All(RootProperties(document), property => Assert.Equal(new XscnKeyRef("StyleBoxFlat1"), property.Value));
    }

    [Fact]
    public void SubResourceUsedOnceInsideAnArrayIsKeyed()
    {
        const string text = """
            [gd_resource type="Resource" format=3]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_a1b2c"]
            bg_color = Color(1, 0, 0, 1)

            [resource]
            metadata/list = [SubResource("StyleBoxFlat_a1b2c")]

            """;

        XscnDocument document = ForwardAndBack(text);

        Assert.Equal("StyleBoxFlat1", Assert.Single(document.Resources).Key);
        GodotArray array = Assert.IsType<GodotArray>(Assert.IsType<XscnScalar>(Assert.Single(RootProperties(document)).Value).Value);
        Assert.Equal(new GodotResourceRef(GodotRefKind.Sub, "StyleBoxFlat1"), Assert.Single(array.Items));
    }

    [Fact]
    public void SubResourceUsedOnceAsABareValueIsInline()
    {
        const string text = """
            [gd_resource type="Resource" format=3]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_a1b2c"]
            bg_color = Color(1, 0, 0, 1)

            [resource]
            metadata/only = SubResource("StyleBoxFlat_a1b2c")

            """;

        XscnDocument document = ForwardAndBack(text);

        Assert.Empty(document.Resources);
        XscnResource inline = Assert.IsType<XscnInline>(Assert.Single(RootProperties(document)).Value).Resource;
        Assert.Null(inline.Key);
        Assert.Equal("StyleBoxFlat", Assert.IsType<XscnClassType>(inline.Type).Class.Name);
        Assert.Equal("bg_color", Assert.Single(inline.Properties).GodotName);
    }

    [Fact]
    public void ResourceNameIsTheKey()
    {
        const string text = """
            [gd_resource type="Resource" format=3]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_a1b2c"]
            resource_name = "Panel"

            [resource]
            metadata/first = SubResource("StyleBoxFlat_a1b2c")
            metadata/second = SubResource("StyleBoxFlat_a1b2c")

            """;

        XscnDocument document = ForwardAndBack(text);

        Assert.Equal("Panel", Assert.Single(document.Resources).Key);
        Assert.All(RootProperties(document), property => Assert.Equal(new XscnKeyRef("Panel"), property.Value));
    }

    // The second "Panel" is taken and "my panel" is not an XML name, so both take the type and a counter.
    [Fact]
    public void TakenOrInvalidResourceNameFallsBackToTheCounter()
    {
        const string text = """
            [gd_resource type="Resource" format=3]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_a1b2c"]
            resource_name = "Panel"

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_d3e4f"]
            resource_name = "Panel"

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_g5h6i"]
            resource_name = "my panel"

            [resource]
            metadata/a = [SubResource("StyleBoxFlat_a1b2c")]
            metadata/b = [SubResource("StyleBoxFlat_d3e4f")]
            metadata/c = [SubResource("StyleBoxFlat_g5h6i")]

            """;

        XscnDocument document = ForwardAndBack(text);

        Assert.Equal(["Panel", "StyleBoxFlat1", "StyleBoxFlat2"], document.Resources.Select(resource => resource.Key));
    }

    [Fact]
    public void ExtResourceInADictionaryIsAResourcePath()
    {
        const string text = """
            [gd_resource type="Resource" format=3]

            [ext_resource type="Texture2D" path="res://icon.png" id="1_ic0n5"]

            [resource]
            metadata/d = {"icon": ExtResource("1_ic0n5")}

            """;

        XscnDocument document = ForwardAndBack(text);

        Assert.Empty(document.Uids);
        GodotDictionary dictionary = Assert.IsType<GodotDictionary>(Assert.IsType<XscnScalar>(Assert.Single(RootProperties(document)).Value).Value);
        GodotPathRef reference = Assert.IsType<GodotPathRef>(Assert.Single(dictionary.Entries).Value);
        Assert.Null(reference.Uid);
        Assert.Equal("res://icon.png", reference.Path);
    }

    [Fact]
    public void ExtResourceUidFillsTheUidsTable()
    {
        const string text = """
            [gd_resource type="Resource" format=3]

            [ext_resource type="Texture2D" uid="uid://b1x2y3" path="res://icon.png" id="1_ic0n5"]

            [resource]
            metadata/icon = ExtResource("1_ic0n5")

            """;

        XscnDocument document = ForwardAndBack(text);

        Assert.Equal(new XscnUid("res://icon.png", "uid://b1x2y3"), Assert.Single(document.Uids));
        Assert.Equal(new XscnFile("res://icon.png"), Assert.Single(RootProperties(document)).Value);
    }

    [Fact]
    public void ExtResourceOfUnknownExtensionTakesThePropertysDeclaredType()
    {
        const string text = """
            [gd_resource type="GradientTexture2D" format=3]

            [ext_resource type="Gradient" path="res://ramp.grad" id="1_gr4d1"]

            [resource]
            gradient = ExtResource("1_gr4d1")

            """;

        ForwardAndBack(text);
    }

    [Fact]
    public void ExtResourceOfUnknownExtensionAndNoDeclaredTypeIsAnIssue()
    {
        const string text = """
            [gd_resource type="Resource" format=3]

            [ext_resource type="Resource" path="res://thing.dat" id="1_th1ng"]

            [resource]
            metadata/thing = ExtResource("1_th1ng")

            """;
        ConversionResult<XscnDocument> forward = TscnToXscn.Convert(TscnReader.Read(text), Metadata, Options);
        Assert.Empty(forward.Issues);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(forward.Document, Metadata, Options);

        ConversionIssue issue = Assert.Single(back.Issues);
        Assert.Equal(ConversionCodes.UnknownExtResourceType, issue.Code);
        Assert.Equal(ConversionSeverity.Warning, issue.Severity);
        Assert.Equal("Resource", Assert.Single(back.Document.ExtResources).Type);
    }

    // Godot's text, as the harness's order input O1: the files are found in sorted property-name order, alpha's first.
    [Fact]
    public void ExtResourcesAreNumberedInSortedPropertyOrder() =>
        ForwardAndBack(
            """
            [gd_resource type="Resource" format=3]

            [ext_resource type="Texture2D" path="res://a.png" id="1_aaaaa"]
            [ext_resource type="Texture2D" path="res://b.png" id="2_bbbbb"]

            [resource]
            metadata/zeta = ExtResource("2_bbbbb")
            metadata/alpha = ExtResource("1_aaaaa")

            """
        );

    // O2: a sub_resource comes after those it uses, which are followed in sorted property-name order.
    [Fact]
    public void SubResourcesComeAfterWhatTheyUseInSortedPropertyOrder() =>
        ForwardAndBack(
            """
            [gd_resource type="Resource" format=3]

            [sub_resource type="Curve" id="Curve_c1111"]

            [sub_resource type="Gradient" id="Gradient_g1111"]

            [sub_resource type="Resource" id="Resource_p1111"]
            metadata/z = SubResource("Gradient_g1111")
            metadata/a = SubResource("Curve_c1111")

            [resource]
            metadata/x = SubResource("Resource_p1111")
            metadata/y = [SubResource("Gradient_g1111")]

            """
        );

    // O3: a file used inside a sub_resource is found when the walk enters it, before a later root property's file.
    [Fact]
    public void ExtResourceFoundInsideASubResourceComesFirst() =>
        ForwardAndBack(
            """
            [gd_resource type="Resource" format=3]

            [ext_resource type="Texture2D" path="res://inner.png" id="1_iiiii"]
            [ext_resource type="Texture2D" path="res://outer.png" id="2_ooooo"]

            [sub_resource type="Resource" id="Resource_s1111"]
            metadata/t = ExtResource("1_iiiii")

            [resource]
            metadata/a = SubResource("Resource_s1111")
            metadata/b = ExtResource("2_ooooo")

            """
        );

    // An editor re-save keeps cached ids, so the sections are in natural id order, not discovery order (m.png is met first).
    [Fact]
    public void KeptExtIdsAreWrittenInNaturalIdOrder()
    {
        const string text = """
            [gd_resource type="Resource" format=3]

            [ext_resource type="Texture2D" path="res://z.png" id="1_abcde"]
            [ext_resource type="Texture2D" path="res://m.png" id="1_qrstu"]

            [resource]
            metadata/m = ExtResource("1_qrstu")
            metadata/z = ExtResource("1_abcde")

            """;

        KeepIdsRoundTrip(text);
    }

    [Fact]
    public void ExtResourceTypeFollowsThePropertysDeclaredTypeBeforeAGenericExtension()
    {
        const string text = """
            [gd_resource type="BoxMesh" format=3]

            [ext_resource type="Material" path="res://look.tres" id="1_l00k1"]

            [resource]
            material = ExtResource("1_l00k1")

            """;

        ForwardAndBack(text);
    }

    [Fact]
    public void PathThatIsNotResStaysAResourceValue()
    {
        const string text = """
            [gd_resource type="Resource" format=3]

            [ext_resource type="Texture2D" path="icon.png" id="1_ic0n5"]

            [resource]
            metadata/icon = ExtResource("1_ic0n5")

            """;

        XscnDocument document = ForwardAndBack(text);

        Assert.Equal(new XscnScalar(new GodotPathRef(null, "icon.png")), Assert.Single(RootProperties(document)).Value);
    }

    [Fact]
    public void UidWrittenInValueTextGoesToTheExtResource()
    {
        XscnDocument document = Xres(Res("Resource", Prop("metadata/list", new GodotArray([new GodotPathRef("uid://b1x2y3", "res://a.png")], null))));

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, Metadata, Options);

        Assert.Empty(back.Issues);
        TscnExtResource ext = Assert.Single(back.Document.ExtResources);
        Assert.Equal(("res://a.png", "uid://b1x2y3"), (ext.Path, ext.Uid));
    }

    // A Theme's type properties (Button/styles/normal) have no attribute name, so no property element can hold an
    // inline resource there: a resource used once by one goes into x:Resources.
    [Fact]
    public void ThemeTypePropertyWithAOnceUsedResourceGoesToTheTable()
    {
        const string text = """
            [gd_resource type="Theme" format=3]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_b1234"]
            bg_color = Color(1, 0, 0, 1)

            [resource]
            Button/styles/normal = SubResource("StyleBoxFlat_b1234")

            """;
        ConversionResult<XscnDocument> forward = TscnToXscn.Convert(TscnReader.Read(text), Metadata, Options);
        Assert.Empty(forward.Issues);

        Assert.Equal("StyleBoxFlat1", Assert.Single(forward.Document.Resources).Key);
        Assert.Equal(new XscnKeyRef("StyleBoxFlat1"), Assert.Single(RootProperties(forward.Document)).Value);
        string written = XscnWriter.Write(forward.Document, Metadata);
        XscnParseResult parsed = XscnParser.Parse(written, XscnDocumentKind.Resource, Metadata);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(written, XscnWriter.Write(parsed.Document, Metadata));
    }

    // Converts the text to an XscnDocument with no issue and back, once with the forward ids, which must write the text
    // again, and once with new ids, which must write it again but for the ids; returns the XscnDocument.
    private static XscnDocument ForwardAndBack(string text)
    {
        XscnDocument document = KeepIdsRoundTrip(text);
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, Metadata, Options);

        Assert.Empty(back.Issues);
        Assert.Equal(Renumber(TscnWriter.Write(TscnReader.Read(text))), Renumber(TscnWriter.Write(back.Document)));
        return document;
    }

    private static XscnDocument KeepIdsRoundTrip(string text)
    {
        TscnDocument source = TscnReader.Read(text);
        ConversionResult<XscnDocument> forward = TscnToXscn.Convert(source, Metadata, Options);
        Assert.Empty(forward.Issues);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(forward.Document, Metadata, Options with { KeepIds = forward.Ids });

        Assert.Empty(back.Issues);
        Assert.Equal(TscnWriter.Write(source), TscnWriter.Write(back.Document));
        return forward.Document;
    }

    private static XscnResource Res(string type, params XscnProperty[] properties) =>
        new() { Type = new XscnClassType(Metadata.FindClass(type)!), Properties = [.. properties] };

    private static XscnProperty Prop(string name, GodotValue value) => new(name, new XscnScalar(value));

    private static XscnDocument Xres(XscnResource root, params XscnResource[] table) => new(XscnDocumentKind.Resource, [], [], [.. table], root);

    private static ImmutableArray<XscnProperty> RootProperties(XscnDocument document) => Assert.IsType<XscnResource>(document.Root).Properties;

    private static string ReadFixture(string file) =>
        new UTF8Encoding(false, true).GetString(File.ReadAllBytes(RepoFiles.PathOf("tests", "fixtures", "scenes", file)));

    // Every section id and every id an ExtResource(…) or SubResource(…) names becomes #1, #2, … in the order the ids
    // first appear, so two files that differ only in their ids are equal.
    private static string Renumber(string text)
    {
        Dictionary<string, string> numbers = new(StringComparer.Ordinal);
        return IdPattern()
            .Replace(text, match => numbers.TryGetValue(match.Value, out string? number) ? number : numbers[match.Value] = "#" + (numbers.Count + 1));
    }

    [GeneratedRegex("""(?<=\bid=")[^"]+(?=")|(?<=(?:Ext|Sub)Resource\(")[^"]+(?=")""")]
    private static partial Regex IdPattern();
}
