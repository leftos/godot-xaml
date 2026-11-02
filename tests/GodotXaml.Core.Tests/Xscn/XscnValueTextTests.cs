using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed class XscnValueTextTests
{
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));
    private static readonly XscnPropertyNames Names = new(Real);

    [Fact]
    public void AVectorIsItsComponents()
    {
        XscnValueType type = TypeOf("Label", "CustomMinimumSize");

        Assert.Equal(new XscnScalar(new GodotReal(GodotRealKind.Vector2, [640, 0])), XscnValueText.Read("640, 0", type));
        Assert.Equal("640, 0", Text(new XscnScalar(new GodotReal(GodotRealKind.Vector2, [640, 0])), type));
        Assert.IsType<XscnInvalid>(XscnValueText.Read("640", type));
        Assert.IsType<XscnInvalid>(XscnValueText.Read("640, x", type));
    }

    [Fact]
    public void AnIntVectorIsItsIntegerComponents()
    {
        XscnValueType type = new("Vector2i", null);

        Assert.Equal(new XscnScalar(new GodotInts(GodotIntKind.Vector2I, [3, -4])), XscnValueText.Read("3, -4", type));
        Assert.Equal("3, -4", Text(new XscnScalar(new GodotInts(GodotIntKind.Vector2I, [3, -4])), type));
        Assert.IsType<XscnInvalid>(XscnValueText.Read("3.5, 4", type));
    }

    [Fact]
    public void AColorIsExactlyFourComponents()
    {
        XscnValueType type = TypeOf("Label", "Modulate");
        var halfWhite = new XscnScalar(new GodotReal(GodotRealKind.Color, [1, 1, 1, 0.5f]));

        Assert.Equal(halfWhite, XscnValueText.Read("1, 1, 1, 0.5", type));
        Assert.Equal("1, 1, 1, 0.5", Text(halfWhite, type));
        XscnInvalid invalid = Assert.IsType<XscnInvalid>(XscnValueText.Read("1, 1, 1", type));
        Assert.Equal("1, 1, 1", invalid.Text);
        Assert.Contains("4", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFloatKeepsItsDigitsAndHasNoTrailingZero()
    {
        XscnValueType type = TypeOf("Control", "AnchorRight");

        XscnValue read = XscnValueText.Read("0.078431375", type);

        Assert.Equal(new XscnScalar(new GodotFloat(0.078431375)), read);
        Assert.Equal("0.078431375", Text(read, type));
        Assert.Equal("1", Text(new XscnScalar(new GodotFloat(1.0)), type));
        Assert.Equal(new XscnScalar(new GodotFloat(1.0)), XscnValueText.Read("1", type));
        Assert.IsType<XscnInvalid>(XscnValueText.Read("one", type));
    }

    [Fact]
    public void ABoolIsTrueOrFalseInEitherCase()
    {
        XscnValueType type = TypeOf("Label", "Visible");

        Assert.Equal(new XscnScalar(new GodotBool(false)), XscnValueText.Read("False", type));
        Assert.Equal(new XscnScalar(new GodotBool(false)), XscnValueText.Read("false", type));
        Assert.Equal("False", Text(new XscnScalar(new GodotBool(false)), type));
        Assert.Equal("True", Text(new XscnScalar(new GodotBool(true)), type));
        Assert.IsType<XscnInvalid>(XscnValueText.Read("yes", type));
    }

    [Fact]
    public void AnEnumIntIsWrittenByName()
    {
        XscnValueType type = TypeOf("Control", "MouseFilter");

        Assert.Equal(new XscnScalar(new GodotInt(2)), XscnValueText.Read("Ignore", type));
        Assert.Equal("Ignore", Text(new XscnScalar(new GodotInt(2)), type));
        Assert.IsType<XscnInvalid>(XscnValueText.Read("Nope", type));
        Assert.Equal("7", Text(new XscnScalar(new GodotInt(7)), TypeOf("Control", "ZIndex")));
    }

    [Theory]
    [InlineData("[b]Bold[/b]")]
    [InlineData("{0} items")]
    [InlineData("\"quoted\"")]
    public void AStringIsLiteralWhateverItsFirstCharacter(string text)
    {
        XscnValueType type = TypeOf("Label", "Text");

        Assert.Equal(new XscnScalar(new GodotString(text)), XscnValueText.Read(text, type));
        Assert.Equal(text, Text(new XscnScalar(new GodotString(text)), type));
    }

    [Fact]
    public void AStringPropertyWithAStringNameDefaultReadsAStringName()
    {
        XscnValueType type = TypeOf("Label", "ThemeTypeVariation");

        Assert.Equal(new XscnScalar(new GodotStringName("DialogShell")), XscnValueText.Read("DialogShell", type));
        Assert.Equal("DialogShell", Text(new XscnScalar(new GodotStringName("DialogShell")), type));
    }

    [Fact]
    public void ANodePathIsTheBarePath()
    {
        XscnValueType type = new("NodePath", null);

        XscnValue read = XscnValueText.Read("../Panel", type);

        Assert.Equal("../Panel", Assert.IsType<GodotNodePath>(Assert.IsType<XscnScalar>(read).Value).Path);
        Assert.Equal("../Panel", Text(read, type));
    }

    [Fact]
    public void AnUnknownTypeIsGodotValueText()
    {
        XscnValueType type = Names.Resolve(Real.FindClass("Node"), "Target").Type;

        XscnValue path = XscnValueText.Read("NodePath(\"../..\")", type);
        XscnValue title = XscnValueText.Read("\"[Esc] to close\"", type);

        Assert.Equal("../..", Assert.IsType<GodotNodePath>(Assert.IsType<XscnScalar>(path).Value).Path);
        Assert.Equal(new XscnScalar(new GodotString("[Esc] to close")), title);
        Assert.Equal("\"[Esc] to close\"", Text(title, type));
        Assert.IsType<XscnInvalid>(XscnValueText.Read("[Esc] to close", type));
    }

    [Fact]
    public void AnObjectIsAFileAKeyOrNull()
    {
        XscnValueType texture = TypeOf("TextureRect", "Texture");
        XscnValueType theme = TypeOf("Control", "Theme");

        Assert.Equal(new XscnFile("res://a.png"), XscnValueText.Read("res://a.png", texture));
        Assert.Equal(new XscnKeyRef("Card"), XscnValueText.Read("{Card}", theme));
        Assert.Equal(new XscnScalar(GodotNull.Instance), XscnValueText.Read("{x:Null}", theme));
        Assert.Equal("res://a.png", Text(new XscnFile("res://a.png"), texture));
        Assert.Equal("{Card}", Text(new XscnKeyRef("Card"), theme));
        Assert.Equal("{x:Null}", Text(new XscnScalar(GodotNull.Instance), theme));
        Assert.IsType<XscnInvalid>(XscnValueText.Read("a.png", texture));
        Assert.IsType<XscnInvalid>(XscnValueText.Read("{}", theme));
    }

    [Fact]
    public void ATransformIsGodotValueText()
    {
        XscnValueType type = TypeOf("Node3D", "Transform");
        const string text = "Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4)";

        XscnValue read = XscnValueText.Read(text, type);

        Assert.IsType<GodotReal>(Assert.IsType<XscnScalar>(read).Value);
        Assert.Equal(text, Text(read, type));
    }

    [Fact]
    public void AFileInsideGodotValueTextIsAResourceAndExtResourceIsRejected()
    {
        XscnValue resource = XscnValueText.Read("[Resource(\"res://a.png\")]", XscnValueType.Unknown);
        XscnValue external = XscnValueText.Read("[ExtResource(\"1\")]", XscnValueType.Unknown);

        Assert.IsType<XscnScalar>(resource);
        XscnInvalid invalid = Assert.IsType<XscnInvalid>(external);
        Assert.Contains("ExtResource", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidValueWritesItsTextBack() =>
        Assert.Equal("1, 1, 1", Text(new XscnInvalid("1, 1, 1", "expected four"), TypeOf("Label", "Modulate")));

    [Fact]
    public void AValueThatDoesNotFitItsCompactFormNeedsXProp()
    {
        AssertNeedsProp(TypeOf("Label", "Text"), new GodotInt(5), "5");
        AssertNeedsProp(TypeOf("Label", "CustomMinimumSize"), new GodotInts(GodotIntKind.Vector2I, [1, 2]), "Vector2i(1, 2)");
        AssertNeedsProp(TypeOf("Control", "AnchorRight"), new GodotInt(1), "1");
        AssertNeedsProp(TypeOf("Label", "ThemeTypeVariation"), new GodotString("Plain"), "\"Plain\"");
        AssertNeedsProp(TypeOf("Control", "Theme"), new GodotInt(3), "3");
    }

    [Fact]
    public void EveryValueThatFitsItsCompactFormRoundTrips()
    {
        (XscnValueType Type, XscnValue Value)[] cases =
        [
            (TypeOf("Label", "Text"), new XscnScalar(new GodotString("a, b"))),
            (TypeOf("Label", "ThemeTypeVariation"), new XscnScalar(new GodotStringName("Title"))),
            (TypeOf("Control", "AnchorRight"), new XscnScalar(new GodotFloat(0.1))),
            (TypeOf("Control", "AnchorRight"), new XscnScalar(new GodotFloat(double.NegativeInfinity))),
            (TypeOf("Label", "Visible"), new XscnScalar(new GodotBool(true))),
            (TypeOf("Control", "SizeFlagsHorizontal"), new XscnScalar(new GodotInt(5))),
            (TypeOf("Control", "AnchorsPreset"), new XscnScalar(new GodotInt(-1))),
            (new XscnValueType("NodePath", null), new XscnScalar(new GodotNodePath("../a:b"))),
            (TypeOf("Label", "CustomMinimumSize"), new XscnScalar(new GodotReal(GodotRealKind.Vector2, [0.1f, -3]))),
            (TypeOf("Label", "Modulate"), new XscnScalar(new GodotReal(GodotRealKind.Color, [0.2f, 0.4f, 0.6f, 1]))),
            (new XscnValueType("Vector3i", null), new XscnScalar(new GodotInts(GodotIntKind.Vector3I, [1, 2, 3]))),
            (TypeOf("Control", "Theme"), new XscnScalar(GodotNull.Instance)),
            (TypeOf("Control", "Theme"), new XscnFile("res://t.tres")),
            (TypeOf("Control", "Theme"), new XscnKeyRef("Card")),
            (TypeOf("Node3D", "Transform"), new XscnScalar(new GodotReal(GodotRealKind.Transform3D, [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4]))),
            (XscnValueType.Unknown, new XscnScalar(new GodotArray([new GodotInt(1), new GodotString("a")], null))),
        ];

        foreach ((XscnValueType type, XscnValue value) in cases)
        {
            Assert.Equal(value, XscnValueText.Read(Text(value, type), type));
        }
    }

    [Fact]
    public void AnIntOnTextOfAnUnknownClassNeedsXPropAndReadsBack()
    {
        XscnValueType type = Names.Resolve(null, Names.AttributeName(null, "text")!).Type;

        XscnNeedsProp prop = Assert.IsType<XscnNeedsProp>(XscnValueText.Write(new XscnScalar(new GodotInt(5)), type));

        Assert.Equal(new XscnScalar(new GodotInt(5)), XscnValueText.Read(prop.GodotText, XscnValueType.Unknown));
    }

    [Fact]
    public void AReferenceOnAnUntypedPropertyRoundTrips()
    {
        Assert.Equal("Resource(\"res://a.png\")", Text(new XscnFile("res://a.png"), XscnValueType.Unknown));
        Assert.Equal("{Card}", Text(new XscnKeyRef("Card"), XscnValueType.Unknown));
        Assert.Equal(new XscnFile("res://a.png"), XscnValueText.Read("Resource(\"res://a.png\")", XscnValueType.Unknown));
        Assert.Equal(new XscnKeyRef("Card"), XscnValueText.Read("{Card}", XscnValueType.Unknown));
        Assert.Equal(new XscnScalar(new GodotDictionary([], null, null)), XscnValueText.Read("{}", XscnValueType.Unknown));
    }

    private static void AssertNeedsProp(XscnValueType type, GodotValue value, string godotText)
    {
        XscnNeedsProp prop = Assert.IsType<XscnNeedsProp>(XscnValueText.Write(new XscnScalar(value), type));
        Assert.Equal(godotText, prop.GodotText);
        Assert.Equal(value, GodotValueReader.Read(prop.GodotText));
    }

    private static string Text(XscnValue value, XscnValueType type) => Assert.IsType<XscnAttributeText>(XscnValueText.Write(value, type)).Text;

    private static XscnValueType TypeOf(string className, string attributeName) => Names.Resolve(Real.FindClass(className)!, attributeName).Type;
}
