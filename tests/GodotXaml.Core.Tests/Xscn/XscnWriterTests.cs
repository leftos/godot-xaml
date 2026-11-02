using System.Text;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Xscn;

public sealed class XscnWriterTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";
    private static readonly GodotMetadata Real = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    public static TheoryData<string> CanonicalFixtures => new(FixtureNames("canonical", "*.x*"));

    public static TheoryData<string> FormatFixtures => new(FixtureNames("format", "*.in.xscn").Select(n => n[..^".in.xscn".Length]));

    [Fact]
    public void FindsTheFixtures()
    {
        Assert.True(CanonicalFixtures.Count >= 6, $"only {CanonicalFixtures.Count} canonical fixtures");
        Assert.True(FormatFixtures.Count >= 3, $"only {FormatFixtures.Count} format pairs");
    }

    // A canonical file parses without a problem and is written back byte for byte.
    [Theory]
    [MemberData(nameof(CanonicalFixtures))]
    public void ACanonicalFileIsWrittenBackByteIdentical(string file)
    {
        byte[] expected = File.ReadAllBytes(FixturePath("canonical", file));
        XscnDocumentKind kind = file.EndsWith(".xres", StringComparison.Ordinal) ? XscnDocumentKind.Resource : XscnDocumentKind.Scene;
        XscnParseResult result = XscnParser.Parse(Encoding.UTF8.GetString(expected), kind, Real);

        Assert.Empty(result.Diagnostics);
        string written = XscnWriter.Write(result.Document, Real);
        Assert.Equal(Encoding.UTF8.GetString(expected), written);
        Assert.Equal(expected, Encoding.UTF8.GetBytes(written));
    }

    // Each .in.xscn is written as its .out.xscn, which is canonical itself.
    [Theory]
    [MemberData(nameof(FormatFixtures))]
    public void AFileIsRewrittenInTheCanonicalLayout(string name)
    {
        string input = File.ReadAllText(FixturePath("format", name + ".in.xscn"));
        string expected = File.ReadAllText(FixturePath("format", name + ".out.xscn"));

        Assert.Equal(expected, Rewrite(input));
        Assert.Equal(expected, Rewrite(expected));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(99)]
    public void AStartTagThatFitsInOneHundredColumnsStaysOnOneLine(int width)
    {
        string line = RootOfWidth(width);

        Assert.Equal(line + "\n", Rewrite(line));
    }

    [Fact]
    public void AStartTagOfOneHundredAndOneColumnsPutsEachAttributeOnItsOwnLine()
    {
        string line = RootOfWidth(101);
        string name = line[(line.IndexOf("Name=", StringComparison.Ordinal) + 6)..^4];
        string expected = $"""
            <Control
                {X}
                Name="{name}" />

            """;

        Assert.Equal(expected, Rewrite(line));
    }

    [Fact]
    public void AnInlineResourceOnAPropertyWithNoAttributeNameIsRejected()
    {
        var resource = new XscnResource { Type = ClassType("StyleBoxFlat") };
        XscnNode root = Root("Control", new XscnProperty("theme_override_styles/bad key", new XscnInline(resource)));

        ArgumentException error = Assert.Throws<ArgumentException>(() => XscnWriter.Write(SceneOf(root), Real));
        Assert.Contains("theme_override_styles/bad key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADefaultValueIsKeptOnAnInstance() =>
        AssertCanonical(
            $"""
            <Control {X} xmlns:c="res://ui/" Name="Root">
              <c:Modal Name="M" FocusMode="All" Visible="True" />
            </Control>

            """
        );

    [Fact]
    public void ADefaultValueIsKeptOnAnOverride() =>
        AssertCanonical(
            $"""
            <Control {X} xmlns:c="res://ui/" Name="Root">
              <c:Modal Name="M">
                <x:Override Path="P" Visible="True" />
              </c:Modal>
            </Control>

            """
        );

    [Fact]
    public void ADefaultValueIsKeptOnAnUnknownClassWhoseTagIsWrittenVerbatim()
    {
        string text = $"""
            <Control {X} Name="Root">
              <Gizmo Name="G" Visible="True" />
            </Control>

            """;
        XscnParseResult result = XscnParser.Parse(text, XscnDocumentKind.Scene, Real);

        Assert.Equal(new XscnUnknownType("Gizmo"), Assert.IsType<XscnNode>(Assert.Single(((XscnNode)result.Document.Root).Children)).Type);
        Assert.Equal(text, XscnWriter.Write(result.Document, Real));
    }

    [Fact]
    public void ADefaultValueIsKeptOnAnInheritedRoot() =>
        AssertCanonical(
            $"""
            <c:Base
                {X}
                xmlns:c="res://ui/"
                Name="Root"
                Visible="True" />

            """
        );

    [Fact]
    public void AScriptPropertyAndAValueThatIsNotAScalarAreKept()
    {
        XscnNode root = Root(
            "Button",
            new XscnProperty("focus_mode", new XscnInvalid("All", "kept as written")),
            new XscnProperty("script", new XscnScalar(GodotNull.Instance))
        );
        string expected = $"""
            <Button
                {X}
                Name="Root"
                Script="{"{x:Null}"}"
                FocusMode="All" />

            """;

        Assert.Equal(expected, XscnWriter.Write(SceneOf(root), Real));
    }

    [Fact]
    public void APlainConnectionToTheRootIsASignalAttribute() =>
        Assert.Equal(
            $"""
            <Button {X} Name="Root" Pressed="OnPressed" />

            """,
            Rewrite(
                $"""
                <Button {X} Name="Root">
                  <x:Connect Signal="Pressed" Method="OnPressed" />
                </Button>
                """
            )
        );

    [Theory]
    [InlineData("""<x:Connect Signal="Pressed" To="Other" Method="M" />""")]
    [InlineData("""<x:Connect Signal="Pressed" Method="M" Flags="Deferred" />""")]
    [InlineData("""<x:Connect Signal="Pressed" Method="M" Flags="0" />""")]
    [InlineData("""<x:Connect Signal="Pressed" Method="M" Binds="[1]" />""")]
    [InlineData("""<x:Connect Signal="Pressed" Method="M" Unbinds="2" />""")]
    [InlineData("""<x:Connect Signal="Pressed" Method="M" FromUidPath="1, 2" ToUidPath="3" />""")]
    public void AConnectionThatIsNotPlainStaysAnElement(string connect) =>
        AssertCanonical(
            $"""
            <Button {X} Name="Root">
              {connect}
            </Button>

            """
        );

    [Fact]
    public void AConnectionOnAnInstanceOrAnUnknownClassStaysAnElement() =>
        AssertCanonical(
            $"""
            <Control {X} Name="Root">
              <Gizmo Name="G">
                <x:Connect Signal="Pressed" Method="M" />
              </Gizmo>
              <x:Instance Name="I" Scene="res://dialog.xscn">
                <x:Connect Signal="Pressed" Method="M" />
              </x:Instance>
            </Control>

            """
        );

    // An attribute would change the call order (attributes are read first, in name order) or repeat a name.
    [Fact]
    public void AConnectionThatWouldReorderOrRepeatAnAttributeStaysAnElement() =>
        AssertCanonical(
            $"""
            <Button {X} Name="Root" Pressed="A">
              <x:Connect Signal="ButtonDown" Method="B" />
              <x:Connect Signal="Pressed" Method="C" />
            </Button>

            """
        );

    [Fact]
    public void AScriptPropertyNamedLikeASignalIsAProp() =>
        AssertCanonical(
            $"""
            <Button {X} Name="Root">
              <x:Prop Name="Pressed" Value="1" />
            </Button>

            """
        );

    [Fact]
    public void APropertyOrConnectionWithLeadingCommentsStaysAnElement() =>
        AssertCanonical(
            $"""
            <Control {X} Name="Root">
              <!-- p -->
              <x:Prop Name="visible" Value="false" />
              <!-- c -->
              <x:Connect Signal="Resized" Method="M" />
            </Control>

            """
        );

    [Fact]
    public void AnEmptyTableWithCommentsIsWritten() =>
        AssertCanonical(
            $"""
            <Control {X} Name="Root">
              <!-- u -->
              <x:Uids />
              <x:Resources>
                <!-- r -->
              </x:Resources>
            </Control>

            """
        );

    // A second property of a name the element already writes as an attribute would repeat the attribute.
    [Theory]
    [InlineData("""<Label {X} Name="Root" Text="a"><x:Prop Name="text" Value='"b"' /></Label>""")]
    [InlineData("""<Control {X} Name="Root"><x:Prop Name="Foo" Value="1" /><x:Prop Name="Foo" Value="2" /></Control>""")]
    [InlineData(
        """<Control {X} Name="Root" ThemeOverrideConstants.separation="1"><x:Prop Name="theme_override_constants/separation" Value="2" /></Control>"""
    )]
    [InlineData("""<Control {X} Name="Root" Script="res://a.gd"><x:Prop Name="script" Value='Resource("res://b.gd")' /></Control>""")]
    public void APropertyWhoseAttributeIsAlreadyWrittenIsAProp(string input)
    {
        string text = input.Replace("{X}", X, StringComparison.Ordinal);
        XscnDocument first = XscnParser.Parse(text, XscnDocumentKind.Scene, Real).Document;

        XscnDocument second = RewriteStably(text);
        Assert.Equal(((XscnNode)first.Root).Properties.Length, ((XscnNode)second.Root).Properties.Length);
    }

    [Fact]
    public void ASceneNamespaceWithTheFormatPrefixGetsAFreePrefix()
    {
        const string F = "https://github.com/leftos/godot-xaml/format/1";
        XscnDocument document = RewriteStably($"""<Control xmlns:f="{F}" xmlns:x="res://ui/" Name="Root"><x:Modal Name="M" /></Control>""");

        Assert.Equal(new XscnSceneType("res://ui/Modal.xscn"), ChildType(document, 0));
    }

    [Fact]
    public void APropertyWithCommentsIsNeverOmitted()
    {
        XscnDocument document = RewriteStably(
            $"""
            <VBoxContainer {X} Name="Root">
              <!-- set on purpose -->
              <x:Prop Name="visible" Value="true" />
              <Label Name="L">
                <!-- container decides this -->
                <x:Prop Name="layout_mode" Value="2" />
              </Label>
            </VBoxContainer>
            """
        );

        var root = (XscnNode)document.Root;
        Assert.Equal([" set on purpose "], Assert.Single(root.Properties).LeadingComments);
        Assert.Equal([" container decides this "], Assert.Single(((XscnNode)root.Children[0]).Properties).LeadingComments);
    }

    [Fact]
    public void AStringWithACharacterXmlCannotHoldIsAPropWithAnEscape()
    {
        XscnNode root = Root("Label", new XscnProperty("text", new XscnScalar(new GodotString("a\u0001b"))));
        string expected = $"""
            <Label {X} Name="Root">
              <x:Prop Name="text" Value='"a\u0001b"' />
            </Label>

            """;

        Assert.Equal(expected, XscnWriter.Write(SceneOf(root), Real));
        Assert.Equal(expected, Rewrite(expected));
    }

    [Fact]
    public void AStringInsideAnArrayWithACharacterXmlCannotHoldIsEscaped()
    {
        var array = new GodotArray([new GodotString("a\u0001b")], null);
        XscnNode root = Root("Control", new XscnProperty("metadata/m", new XscnScalar(array)));
        string expected = $"""
            <Control {X} Name="Root">
              <x:Prop Name="metadata/m" Value='["a\u0001b"]' />
            </Control>

            """;

        Assert.Equal(expected, XscnWriter.Write(SceneOf(root), Real));
        XscnDocument reread = RewriteStably(expected);
        Assert.Equal(new XscnScalar(array), Assert.Single(((XscnNode)reread.Root).Properties).Value);
    }

    [Fact]
    public void ANodeNameWithACharacterXmlCannotHoldIsRejected()
    {
        XscnNode root = Root("Control") with { Name = "a\u0001b" };

        ArgumentException error = Assert.Throws<ArgumentException>(() => XscnWriter.Write(SceneOf(root), Real));
        Assert.Contains("Name", error.Message, StringComparison.Ordinal);
        Assert.Contains("<Control>", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("tooltip_text")]
    public void ALoneSurrogateInAValueIsRejected(string property)
    {
        XscnNode root = Root("Label", new XscnProperty(property, new XscnScalar(new GodotString("a\uD800b"))));

        ArgumentException error = Assert.Throws<ArgumentException>(() => XscnWriter.Write(SceneOf(root), Real));
        Assert.Contains(property, error.Message, StringComparison.Ordinal);
        Assert.Contains("<Label>", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutModeIsKeptWhereTheClassHasNoSuchPropertyOrTheValueIsNotAScalar()
    {
        string text = $"""
            <HBoxContainer {X} Name="Root">
              <x:Resources>
                <StyleBoxFlat x:Key="K" />
              </x:Resources>
              <x:Prop Name="layout_mode" Value="{"{K}"}" />
              <Timer Name="T" Script="res://t.gd">
                <x:Prop Name="layout_mode" Value="5" />
              </Timer>
              <Control Name="C">
                <x:Prop Name="layout_mode" Value="{"{K}"}" />
              </Control>
            </HBoxContainer>
            """;

        var root = (XscnNode)RewriteStably(text).Root;
        Assert.Equal(2, root.Children.Sum(c => c.Properties.Count(p => p.GodotName == "layout_mode")));
    }

    [Fact]
    public void ACommentWithCarriageReturnsIsWrittenWithLineFeeds()
    {
        string text = $"<Control {X} Name=\"Root\">\r\n  <!-- one\r\n       two\rthree -->\r\n  <Label Name=\"L\" />\r\n</Control>\r\n";

        string written = XscnWriter.Write(RewriteStably(text), Real);
        Assert.DoesNotContain('\r', written);
        Assert.Contains("<!-- one\n       two\nthree -->", written, StringComparison.Ordinal);
    }

    // The reader takes attributes before elements, so a name with more than one property is all elements, in model order,
    // and every last value survives: an attribute would win where the model's last property does.
    [Theory]
    [InlineData("""<Label {X} Name="Root"><x:Prop Name="text" Value='"a\u0001b"' /><x:Prop Name="text" Value='"c"' /></Label>""")]
    [InlineData("""<Label {X} Name="Root"><!-- keep --><x:Prop Name="text" Value='"b"' /><x:Prop Name="text" Value='"a"' /></Label>""")]
    [InlineData("""<Button {X} Name="Root"><x:Prop Name="script" Value='"res://b.gd"' /><x:Prop Name="script" Value='"res://a.gd"' /></Button>""")]
    [InlineData(
        """<Control {X} Name="Root"><x:Resources><Theme x:Key="K" /></x:Resources>"""
            + """<Control.Theme><Theme DefaultFontSize="3" /></Control.Theme>"""
            + """<x:Prop Name="theme" Value="{K}" /></Control>"""
    )]
    [InlineData("""<Label {X} Name="Root" Text="a"><x:Prop Name="text" Value='""' /></Label>""")]
    public void EveryPropertyOfARepeatedNameIsAnElementAndTheLastOneStillWins(string input)
    {
        string text = input.Replace("{X}", X, StringComparison.Ordinal);
        var first = (XscnNodeElement)XscnParser.Parse(text, XscnDocumentKind.Scene, Real).Document.Root;
        var second = (XscnNodeElement)RewriteStably(text).Root;

        Dictionary<string, XscnValue> expected = LastValues(first);
        Dictionary<string, XscnValue> actual = LastValues(second);
        Assert.Equal([.. expected.Keys.Order(StringComparer.Ordinal)], [.. actual.Keys.Order(StringComparer.Ordinal)]);
        foreach (string name in expected.Keys)
        {
            Assert.Equal(expected[name], actual[name]);
        }

        Assert.Equal(first.Properties.Length, second.Properties.Length);
    }

    [Fact]
    public void APropertyWithTrailingCommentsIsAPropAndKeepsThem()
    {
        XscnNode root = Root("Label", new XscnProperty("text", new XscnScalar(new GodotString("hi"))) { TrailingComments = [" trail "] });
        string expected = $"""
            <Label {X} Name="Root">
              <x:Prop Name="text" Value='"hi"' />
              <!-- trail -->
            </Label>

            """;

        Assert.Equal(expected, XscnWriter.Write(SceneOf(root), Real));

        // The parser gives a comment after the last element to its parent, which writes it back in the same place.
        XscnDocument reread = RewriteStably(expected);
        Assert.Equal([" trail "], ((XscnNode)reread.Root).TrailingComments);
    }

    [Fact]
    public void APropertyWithBothLeadingAndTrailingCommentsKeepsBoth()
    {
        var property = new XscnProperty("text", new XscnScalar(new GodotString("hi")))
        {
            LeadingComments = [" lead "],
            TrailingComments = [" trail "],
        };
        string expected = $"""
            <Label {X} Name="Root">
              <!-- lead -->
              <x:Prop Name="text" Value='"hi"' />
              <!-- trail -->
            </Label>

            """;

        Assert.Equal(expected, XscnWriter.Write(SceneOf(Root("Label", property)), Real));
        Assert.Equal(expected, Rewrite(expected));
    }

    [Fact]
    public void AForbiddenCharacterInABindIsEscapedAndReadsBack()
    {
        XscnNode root = Root("Button") with { Connections = [BoundConnection(new GodotString("a\u0001"))] };
        string written = XscnWriter.Write(SceneOf(root), Real);

        Assert.Contains("Binds='[\"a\\u0001\"]'", written, StringComparison.Ordinal);
        XscnConnection reread = Assert.Single(((XscnNode)RewriteStably(written).Root).Connections);
        Assert.Equal(new GodotString("a\u0001"), Assert.Single(reread.Binds));
    }

    [Fact]
    public void ALoneSurrogateInABindIsRejected()
    {
        XscnNode root = Root("Button") with { Connections = [BoundConnection(new GodotString("a\uD800"))] };

        ArgumentException error = Assert.Throws<ArgumentException>(() => XscnWriter.Write(SceneOf(root), Real));
        Assert.Contains("Binds", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGroupOrNodePathNameWithAForbiddenCharacterIsArrayText(bool groups)
    {
        XscnNode root = groups ? Root("Control") with { Groups = ["g\u0007"] } : Root("Control") with { NodePaths = ["g\u0007"] };
        string name = groups ? "x:Groups" : "x:NodePaths";
        string written = XscnWriter.Write(SceneOf(root), Real);

        Assert.Contains(name + "='[\"g\\u0007\"]'", written, StringComparison.Ordinal);
        var reread = (XscnNode)RewriteStably(written).Root;
        Assert.Equal(["g\u0007"], groups ? reread.Groups : reread.NodePaths);
    }

    [Fact]
    public void ALoneSurrogateInAGroupNameIsRejected()
    {
        XscnNode root = Root("Control") with { Groups = ["g\uDC00"] };

        ArgumentException error = Assert.Throws<ArgumentException>(() => XscnWriter.Write(SceneOf(root), Real));
        Assert.Contains("x:Groups", error.Message, StringComparison.Ordinal);
    }

    // Parses, writes, and parses the written text again: the re-read has no diagnostic and writes the same text.
    private static XscnDocument RewriteStably(string text)
    {
        string written = XscnWriter.Write(XscnParser.Parse(text, XscnDocumentKind.Scene, Real).Document, Real);
        XscnParseResult reread = XscnParser.Parse(written, XscnDocumentKind.Scene, Real);
        Assert.Empty(reread.Diagnostics);
        Assert.Equal(written, XscnWriter.Write(reread.Document, Real));
        return reread.Document;
    }

    private static XscnType ChildType(XscnDocument document, int index) => ((XscnNode)((XscnNode)document.Root).Children[index]).Type;

    private static IEnumerable<string> FixtureNames(string folder, string pattern) =>
        Directory
            .GetFiles(RepoFiles.PathOf("tests", "fixtures", "xscn", folder), pattern)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal);

    private static string FixturePath(string folder, string file) => RepoFiles.PathOf("tests", "fixtures", "xscn", folder, file);

    // A root Control whose start tag, written on one line, is the given width.
    private static string RootOfWidth(int width)
    {
        string head = $"<Control {X} Name=\"";
        const string Tail = "\" />";
        return head + new string('a', width - head.Length - Tail.Length) + Tail;
    }

    private static string Rewrite(string text)
    {
        XscnParseResult result = XscnParser.Parse(text, XscnDocumentKind.Scene, Real);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
        return XscnWriter.Write(result.Document, Real);
    }

    private static void AssertCanonical(string text) => Assert.Equal(text, Rewrite(text));

    private static XscnClassType ClassType(string name) => new(Real.FindClass(name)!);

    private static XscnNode Root(string cls, params XscnProperty[] properties) =>
        new()
        {
            Type = ClassType(cls),
            Name = "Root",
            Properties = [.. properties],
        };

    private static XscnDocument SceneOf(XscnElement root) => new(XscnDocumentKind.Scene, [], [], [], root);

    // The value each Godot name ends up with, a later property of the name winning, as the reader's last one does.
    private static Dictionary<string, XscnValue> LastValues(XscnNodeElement element)
    {
        Dictionary<string, XscnValue> values = new(StringComparer.Ordinal);
        foreach (XscnProperty property in element.Properties)
        {
            values[property.GodotName] = property.Value;
        }

        return values;
    }

    private static XscnConnection BoundConnection(params GodotValue[] binds) => new("pressed", ".", "M", 2, [.. binds], 0, [], []);
}
