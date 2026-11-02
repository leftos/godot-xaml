using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// A forward conversion keeps the comments of the document it replaces, on the parts of the new document that match them
// by path, key, property name and connection triple (docs/DECISIONS.md, "Canonical formatting on save").
// A written document ends with a newline, which a raw string literal leaves out before its closing quotes; each expected
// text of a whole document adds it back.
public sealed class PreviousCommentsTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    private const string ControlRoot = """
        [gd_scene format=3]

        [node name="Root" type="Control"]
        """;

    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new();

    [Fact]
    public void KeepsACommentBeforeTheRoot()
    {
        string previous = $$"""
            <!-- about the root -->
            <Control {{X}} Name="Root" />
            """;

        Assert.Equal(
            $$"""
            <!-- about the root -->
            <Control {{X}} Name="Root" />
            """ + "\n",
            Forward(previous, ControlRoot)
        );
    }

    [Fact]
    public void KeepsACommentBeforeAChildNode()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about Go -->
              <Button Name="Go" />
            </Control>
            """;

        Assert.Equal(
            $$"""
            <Control {{X}} Name="Root">
              <!-- about Go -->
              <Button Name="Go" />
            </Control>
            """ + "\n",
            Forward(previous, WithChild("""[node name="Go" type="Button" parent="."]"""))
        );
    }

    [Fact]
    public void KeepsATrailingCommentInsideANode()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <Button Name="Go">
                <!-- inside Go -->
              </Button>
            </Control>
            """;

        Assert.Equal(
            $$"""
            <Control {{X}} Name="Root">
              <Button Name="Go">
                <!-- inside Go -->
              </Button>
            </Control>
            """ + "\n",
            Forward(previous, WithChild("""[node name="Go" type="Button" parent="."]"""))
        );
    }

    [Fact]
    public void KeepsACommentBeforeAProperty()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about focus -->
              <x:Prop Name="focus_mode" Value="2" />
            </Control>
            """;

        Assert.Equal(
            $$"""
            <Control {{X}} Name="Root">
              <!-- about focus -->
              <x:Prop Name="focus_mode" Value="2" />
            </Control>
            """ + "\n",
            Forward(previous, ControlRoot + "\nfocus_mode = 2\n")
        );
    }

    [Fact]
    public void KeepsACommentBeforeAConnection()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about resized -->
              <x:Connect Signal="Resized" Method="OnResized" />
            </Control>
            """;

        Assert.Equal(
            $$"""
            <Control {{X}} Name="Root">
              <!-- about resized -->
              <x:Connect Signal="Resized" Method="OnResized" />
            </Control>
            """ + "\n",
            Forward(previous, ControlRoot + "\n" + Connection)
        );
    }

    [Fact]
    public void KeepsTwoConnectionCommentsBySameTriple()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- first -->
              <x:Connect Signal="Resized" Method="OnResized" />
              <!-- second -->
              <x:Connect Signal="Resized" Method="OnResized" />
            </Control>
            """;

        Assert.Equal(
            $$"""
            <Control {{X}} Name="Root">
              <!-- first -->
              <x:Connect Signal="Resized" Method="OnResized" />
              <!-- second -->
              <x:Connect Signal="Resized" Method="OnResized" />
            </Control>
            """ + "\n",
            Forward(previous, ControlRoot + "\n" + Connection + "\n" + Connection)
        );
    }

    [Fact]
    public void KeepsCommentsOnTheUidsAndResourcesTables()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- uids lead -->
              <x:Uids>
                <!-- about frame -->
                <x:Uid Path="res://art/frame.png" Value="uid://dframe" />
                <!-- uids trail -->
              </x:Uids>
              <!-- resources lead -->
              <x:Resources>
                <!-- about panel -->
                <StyleBoxFlat x:Key="Panel" BgColor="0, 0, 0, 1" />
                <!-- resources trail -->
              </x:Resources>
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [ext_resource type="Texture2D" path="res://art/frame.png" id="1" uid="uid://dframe"]

            [sub_resource type="StyleBoxFlat" id="2"]
            bg_color = Color(0, 0, 0, 1)

            [node name="Root" type="Control"]

            [node name="Frame" type="TextureRect" parent="."]
            texture = ExtResource("1")

            [node name="PanelA" type="PanelContainer" parent="."]
            theme_override_styles/panel = SubResource("2")

            [node name="PanelB" type="PanelContainer" parent="."]
            theme_override_styles/panel = SubResource("2")
            """;

        Assert.Contains(
            """
              <!-- uids lead -->
              <x:Uids>
                <!-- about frame -->
                <x:Uid Path="res://art/frame.png" Value="uid://dframe" />
                <!-- uids trail -->
              </x:Uids>
              <!-- resources lead -->
              <x:Resources>
                <!-- about panel -->
                <StyleBoxFlat x:Key="Panel" BgColor="0, 0, 0, 1" />
                <!-- resources trail -->
              </x:Resources>
            """ + "\n",
            Forward(previous, tscn),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void KeepsACommentBeforeAPropertyOfAnInlineResource()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <Control.Theme>
                <Theme>
                  <!-- about the font size -->
                  <x:Prop Name="default_font_size" Value="20" />
                </Theme>
              </Control.Theme>
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [sub_resource type="Theme" id="1"]
            default_font_size = 20

            [node name="Root" type="Control"]
            theme = SubResource("1")
            """;

        Assert.Equal(
            $$"""
            <Control {{X}} Name="Root">
              <Control.Theme>
                <Theme>
                  <!-- about the font size -->
                  <x:Prop Name="default_font_size" Value="20" />
                </Theme>
              </Control.Theme>
            </Control>
            """ + "\n",
            Forward(previous, tscn)
        );
    }

    [Fact]
    public void KeepsCommentsWhenChildrenAreReordered()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about A -->
              <Button Name="A" />
              <!-- about B -->
              <Button Name="B" />
            </Control>
            """;

        string tscn = WithChild(
            """
            [node name="B" type="Button" parent="."]

            [node name="A" type="Button" parent="."]
            """
        );

        string text = Forward(previous, tscn);

        Assert.Contains("<!-- about B -->\n  <Button Name=\"B\" />", text, StringComparison.Ordinal);
        Assert.Contains("<!-- about A -->\n  <Button Name=\"A\" />", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("about B", StringComparison.Ordinal) < text.IndexOf("about A", StringComparison.Ordinal));
    }

    [Fact]
    public void DropsTheCommentOfARemovedNode()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about A -->
              <Button Name="A" />
              <!-- about B -->
              <Button Name="B" />
            </Control>
            """;

        string text = Forward(previous, WithChild("""[node name="A" type="Button" parent="."]"""));

        Assert.Contains("<!-- about A -->", text, StringComparison.Ordinal);
        Assert.DoesNotContain("about B", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DropsTheCommentOfARenamedNode()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about the old name -->
              <Button Name="Old" />
            </Control>
            """;

        Assert.DoesNotContain("<!--", Forward(previous, WithChild("""[node name="New" type="Button" parent="."]""")), StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsTheCommentsOfAnXresRoot()
    {
        string previous = $$"""
            <!-- about the resource -->
            <StyleBoxFlat {{X}} BgColor="1, 0, 0, 1">
              <!-- end of the resource -->
            </StyleBoxFlat>
            """;

        string tres = """
            [gd_resource type="StyleBoxFlat" format=3]

            [resource]
            bg_color = Color(1, 0, 0, 1)
            """;

        string text = Forward(previous, tres, XscnDocumentKind.Resource);

        Assert.StartsWith("<!-- about the resource -->\n<StyleBoxFlat", text);
        Assert.Contains("<!-- end of the resource -->\n</StyleBoxFlat>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertsWithoutCommentsWhenThereIsNoPrevious()
    {
        ConversionResult<XscnDocument> forward = TscnToXscn.Convert(TscnReader.Read(ControlRoot), Metadata, Options);

        Assert.Empty(forward.Issues);
        Assert.DoesNotContain("<!--", XscnWriter.Write(forward.Document, Metadata), StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotCarryCommentsToANewResourceWithADeletedEntrysKey()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <x:Resources>
                <!-- old -->
                <StyleBoxFlat x:Key="StyleBoxFlat1" BgColor="1, 0, 0, 1" />
              </x:Resources>
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [sub_resource type="StyleBoxFlat" id="1"]
            bg_color = Color(0, 0, 1, 1)

            [node name="Root" type="Control"]

            [node name="PanelA" type="PanelContainer" parent="."]
            theme_override_styles/panel = SubResource("1")

            [node name="PanelB" type="PanelContainer" parent="."]
            theme_override_styles/panel = SubResource("1")
            """;

        string text = Forward(previous, tscn);

        Assert.Contains("<StyleBoxFlat x:Key=\"StyleBoxFlat1\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DropsAPropertysTrailingCommentWhenItsValueIsNoLongerInline()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <Control.Theme>
                <Theme />
                <!-- after the theme -->
              </Control.Theme>
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [sub_resource type="Theme" id="1"]
            default_font_size = 20

            [node name="Root" type="Control"]
            theme = SubResource("1")

            [node name="Other" type="Control" parent="."]
            theme = SubResource("1")
            """;

        Assert.DoesNotContain("after the theme", Forward(previous, tscn), StringComparison.Ordinal);
    }

    [Fact]
    public void DropsTheCommentsOfAnInlineResourceOfAnotherClass()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <Control.Theme>
                <!-- about the theme -->
                <Theme />
              </Control.Theme>
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [sub_resource type="StyleBoxFlat" id="1"]
            bg_color = Color(0, 0, 0, 1)

            [node name="Root" type="Control"]
            theme = SubResource("1")
            """;

        string text = Forward(previous, tscn);

        Assert.Contains("<Control.Theme>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("about the theme", text, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresAPreviousDocumentOfTheOtherKind()
    {
        string previous = $$"""
            <!-- about the resource -->
            <StyleBoxFlat {{X}} BgColor="1, 0, 0, 1">
              <!-- resources lead -->
              <x:Resources>
                <!-- about the panel -->
                <StyleBoxFlat x:Key="Panel" BgColor="0, 0, 0, 1" />
              </x:Resources>
            </StyleBoxFlat>
            """;

        string tscn = """
            [gd_scene format=3]

            [sub_resource type="StyleBoxFlat" id="1"]
            bg_color = Color(0, 0, 0, 1)

            [node name="Root" type="Control"]

            [node name="PanelA" type="PanelContainer" parent="."]
            theme_override_styles/panel = SubResource("1")

            [node name="PanelB" type="PanelContainer" parent="."]
            theme_override_styles/panel = SubResource("1")
            """;

        Assert.DoesNotContain("<!--", Forward(previous, tscn, XscnDocumentKind.Resource), StringComparison.Ordinal);
    }

    private const string Connection = """
        [connection signal="resized" from="." to="." method="OnResized"]
        """;

    // A scene whose root is a Control, with sections appended below it.
    private static string WithChild(string sections) => ControlRoot + "\n" + sections + "\n";

    // The text of the save that replaces the previous document: its .tscn converted forward with Previous, then written.
    private static string Forward(string previousText, string tscnText, XscnDocumentKind kind = XscnDocumentKind.Scene)
    {
        XscnParseResult parsed = XscnParser.Parse(previousText, kind, Metadata);
        Assert.False(parsed.HasErrors);
        ConversionResult<XscnDocument> forward = TscnToXscn.Convert(TscnReader.Read(tscnText), Metadata, Options with { Previous = parsed.Document });
        Assert.Empty(forward.Issues);
        return XscnWriter.Write(forward.Document, Metadata);
    }
}
