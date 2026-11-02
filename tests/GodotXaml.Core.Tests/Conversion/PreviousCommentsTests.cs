using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// A forward conversion keeps the comments of the document it replaces, on the parts of the new document that match them
// by unique id or path, key, property name and connection triple, and reports with GXC0117 every comment it cannot
// carry (docs/DECISIONS.md, "Canonical formatting on save").
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

        ConversionResult<XscnDocument> forward = Converted(previous, WithChild("""[node name="A" type="Button" parent="."]"""));
        string text = XscnWriter.Write(forward.Document, Metadata);

        Assert.Contains("<!-- about A -->", text, StringComparison.Ordinal);
        Assert.DoesNotContain("about B", text, StringComparison.Ordinal);
        ConversionIssue issue = Dropped(forward);
        Assert.Contains("node B", issue.Message, StringComparison.Ordinal);
        Assert.Equal("B", issue.Location);
    }

    [Fact]
    public void DropsTheCommentOfARenamedNodeWithoutAUniqueId()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about the old name -->
              <Button Name="Old" />
            </Control>
            """;

        ConversionResult<XscnDocument> forward = Converted(previous, WithChild("""[node name="New" type="Button" parent="."]"""));

        Assert.DoesNotContain("<!--", XscnWriter.Write(forward.Document, Metadata), StringComparison.Ordinal);
        ConversionIssue issue = Dropped(forward);
        Assert.Contains("node Old", issue.Message, StringComparison.Ordinal);
        Assert.Equal("Old", issue.Location);
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

        ConversionResult<XscnDocument> forward = Converted(previous, tscn);
        string text = XscnWriter.Write(forward.Document, Metadata);

        Assert.Contains("<StyleBoxFlat x:Key=\"StyleBoxFlat1\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old", text, StringComparison.Ordinal);
        ConversionIssue issue = Dropped(forward);
        Assert.Contains("the x:Resources entry StyleBoxFlat1", issue.Message, StringComparison.Ordinal);
        Assert.Equal("StyleBoxFlat1", issue.Location);
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

        ConversionResult<XscnDocument> forward = Converted(previous, tscn);

        Assert.DoesNotContain("after the theme", XscnWriter.Write(forward.Document, Metadata), StringComparison.Ordinal);
        ConversionIssue issue = Dropped(forward);
        Assert.Contains("property theme of .", issue.Message, StringComparison.Ordinal);
        Assert.Contains("its value is no longer an inline resource", issue.Message, StringComparison.Ordinal);
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

        ConversionResult<XscnDocument> forward = Converted(previous, tscn);
        string text = XscnWriter.Write(forward.Document, Metadata);

        Assert.Contains("<Control.Theme>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("about the theme", text, StringComparison.Ordinal);
        ConversionIssue issue = Dropped(forward);
        Assert.Contains("its inline resource is now a StyleBoxFlat", issue.Message, StringComparison.Ordinal);
        Assert.Equal(".", issue.Location);
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

        ConversionResult<XscnDocument> forward = Converted(previous, tscn, XscnDocumentKind.Resource);
        ConversionIssue[] dropped = [.. forward.Issues.Where(issue => issue.Code == ConversionCodes.CommentDropped)];

        Assert.DoesNotContain("<!--", XscnWriter.Write(forward.Document, Metadata), StringComparison.Ordinal);
        Assert.Equal(3, dropped.Length);
        Assert.Contains(dropped, issue => issue.Location == "." && issue.Message.Contains("the resource root", StringComparison.Ordinal));
        Assert.Contains(
            dropped,
            issue => issue.Location == "x:Resources" && issue.Message.Contains("the x:Resources table", StringComparison.Ordinal)
        );
        Assert.Contains(
            dropped,
            issue => issue.Location == "Panel" && issue.Message.Contains("the x:Resources entry Panel", StringComparison.Ordinal)
        );
        Assert.All(dropped, issue => Assert.Contains("the file being replaced is a resource", issue.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void KeepsTheCommentOfARenamedNodeByItsUniqueId()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about the label -->
              <Label Name="Old" x:UniqueId="5" />
            </Control>
            """;

        Assert.Equal(
            $$"""
            <Control {{X}} Name="Root">
              <!-- about the label -->
              <Label Name="New" x:UniqueId="5" />
            </Control>
            """ + "\n",
            Forward(previous, WithChild("""[node name="New" type="Label" parent="." unique_id=5]"""))
        );
    }

    [Fact]
    public void KeepsTheCommentOfAMovedNodeByItsUniqueId()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about the label -->
              <Label Name="Old" x:UniqueId="5" />
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [node name="Root" type="Control"]

            [node name="Box" type="Control" parent="."]

            [node name="New" type="Label" parent="Box" unique_id=5]
            """;

        Assert.Equal(
            $$"""
            <Control {{X}} Name="Root">
              <Control Name="Box">
                <!-- about the label -->
                <Label Name="New" x:UniqueId="5" />
              </Control>
            </Control>
            """ + "\n",
            Forward(previous, tscn)
        );
    }

    [Fact]
    public void MatchesByPathWhenAUniqueIdRepeats()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about P -->
              <Button Name="P" x:UniqueId="5" />
              <!-- about Q -->
              <Button Name="Q" x:UniqueId="5" />
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [node name="Root" type="Control"]

            [node name="P" type="Button" parent="." unique_id=5]

            [node name="Z" type="Button" parent="." unique_id=5]
            """;

        ConversionResult<XscnDocument> forward = Converted(previous, tscn);
        string text = XscnWriter.Write(forward.Document, Metadata);

        Assert.Contains("<!-- about P -->\n  <Button Name=\"P\" x:UniqueId=\"5\" />", text, StringComparison.Ordinal);
        Assert.DoesNotContain("about Q", text, StringComparison.Ordinal);
        ConversionIssue issue = Dropped(forward);
        Assert.Contains("node Q", issue.Message, StringComparison.Ordinal);
        Assert.Equal("Q", issue.Location);
    }

    [Fact]
    public void DoesNotMatchOneElementTwice()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about the label -->
              <Label Name="Old" x:UniqueId="5" />
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [node name="Root" type="Control"]

            [node name="New" type="Label" parent="." unique_id=5]

            [node name="Old" type="Label" parent="."]
            """;

        string text = Forward(previous, tscn);

        Assert.Equal(1, text.Split("about the label").Length - 1);
        Assert.Contains("<!-- about the label -->\n  <Label Name=\"New\" x:UniqueId=\"5\" />", text, StringComparison.Ordinal);
        Assert.Contains("<Label Name=\"Old\" />", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsADroppedConnectionComment()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about resized -->
              <x:Connect Signal="Resized" Method="OnResized" />
            </Control>
            """;

        ConversionResult<XscnDocument> forward = Converted(previous, ControlRoot);

        Assert.DoesNotContain("about resized", XscnWriter.Write(forward.Document, Metadata), StringComparison.Ordinal);
        ConversionIssue issue = Dropped(forward);
        Assert.Contains("connection resized -> OnResized of .", issue.Message, StringComparison.Ordinal);
        Assert.Equal(".", issue.Location);
    }

    [Fact]
    public void ReportsTheTableCommentsWhenTheTableIsGone()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- uids lead -->
              <x:Uids>
                <x:Uid Path="res://art/frame.png" Value="uid://dframe" />
                <!-- uids trail -->
              </x:Uids>
              <!-- resources lead -->
              <x:Resources>
                <StyleBoxFlat x:Key="Panel" BgColor="0, 0, 0, 1" />
                <!-- resources trail -->
              </x:Resources>
            </Control>
            """;

        ConversionResult<XscnDocument> forward = Converted(previous, ControlRoot);
        ConversionIssue[] dropped = [.. forward.Issues.Where(issue => issue.Code == ConversionCodes.CommentDropped)];

        Assert.Equal(2, dropped.Length);
        Assert.Contains(dropped, issue => issue.Location == "x:Uids" && issue.Message.Contains("the x:Uids table", StringComparison.Ordinal));
        Assert.Contains(
            dropped,
            issue => issue.Location == "x:Resources" && issue.Message.Contains("the x:Resources table", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void CutsALongDroppedCommentInTheWarning()
    {
        string comment = new('x', 70);
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- {{comment}} -->
              <Button Name="B" />
            </Control>
            """;

        ConversionIssue issue = Dropped(Converted(previous, ControlRoot));

        Assert.Contains(": \"" + new string('x', 60) + "…\"", issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 61), issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotReportAChildKeptByItsUniqueIdUnderARenamedParent()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- a -->
              <Control Name="A">
                <!-- b -->
                <Button Name="B" x:UniqueId="7" />
              </Control>
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [node name="Root" type="Control"]

            [node name="A2" type="Control" parent="."]

            [node name="B" type="Button" parent="A2" unique_id=7]
            """;

        ConversionResult<XscnDocument> forward = Converted(previous, tscn);
        string text = XscnWriter.Write(forward.Document, Metadata);

        Assert.Contains("<!-- b -->\n    <Button Name=\"B\" x:UniqueId=\"7\" />", text, StringComparison.Ordinal);
        ConversionIssue issue = Dropped(forward);
        Assert.Contains("node A", issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("node B", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotReportAChildLaterClaimedByPath()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <Control Name="P" x:UniqueId="1">
                <!-- about C -->
                <Button Name="C" />
              </Control>
              <Control Name="Z" x:UniqueId="2" />
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [node name="Root" type="Control"]

            [node name="Z" type="Control" parent="." unique_id=1]

            [node name="P" type="Control" parent="." unique_id=2]

            [node name="C" type="Button" parent="P"]
            """;

        string text = Forward(previous, tscn);

        Assert.Contains("<!-- about C -->\n    <Button Name=\"C\" />", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsTheCommentOnAPropertyOfADeletedResourceEntry()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <x:Resources>
                <StyleBoxFlat x:Key="Panel">
                  <!-- about the corner -->
                  <x:Prop Name="corner_radius_top_left" Value="4" />
                </StyleBoxFlat>
              </x:Resources>
            </Control>
            """;

        ConversionIssue issue = Dropped(Converted(previous, ControlRoot));

        Assert.Contains("property corner_radius_top_left of Panel", issue.Message, StringComparison.Ordinal);
        Assert.Equal("Panel", issue.Location);
    }

    [Fact]
    public void ReportsADroppedChildAtItsPreviousPath()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <Control Name="Old" x:UniqueId="5">
                <!-- about C -->
                <Button Name="C" />
              </Control>
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [node name="Root" type="Control"]

            [node name="New" type="Control" parent="." unique_id=5]
            """;

        ConversionIssue issue = Dropped(Converted(previous, tscn));

        Assert.Contains("node Old/C", issue.Message, StringComparison.Ordinal);
        Assert.Equal("Old/C", issue.Location);
    }

    [Fact]
    public void NamesInnerPropertiesByTheirOuterProperty()
    {
        string previous = $$"""
            <Button {{X}} Name="Root">
              <Button.ThemeOverrideStyles.normal>
                <StyleBoxFlat>
                  <!-- about normal -->
                  <x:Prop Name="corner_radius_top_left" Value="4" />
                </StyleBoxFlat>
              </Button.ThemeOverrideStyles.normal>
              <Button.ThemeOverrideStyles.focus>
                <StyleBoxFlat>
                  <!-- about focus -->
                  <x:Prop Name="corner_radius_top_right" Value="4" />
                </StyleBoxFlat>
              </Button.ThemeOverrideStyles.focus>
            </Button>
            """;

        string tscn = """
            [gd_scene format=3]

            [node name="Root" type="Button"]
            """;

        ConversionResult<XscnDocument> forward = Converted(previous, tscn);
        ConversionIssue[] dropped = [.. forward.Issues.Where(issue => issue.Code == ConversionCodes.CommentDropped)];

        Assert.Equal(2, dropped.Length);
        Assert.Contains(
            dropped,
            issue => issue.Message.Contains("property corner_radius_top_left of theme_override_styles/normal of .", StringComparison.Ordinal)
        );
        Assert.Contains(
            dropped,
            issue => issue.Message.Contains("property corner_radius_top_right of theme_override_styles/focus of .", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void CarriesTheCommentOfARepeatedSiblingNameByItsUniqueId()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <Node Name="A" />
              <!-- about the second A -->
              <Node Name="A" x:UniqueId="7" />
            </Control>
            """;

        string tscn = """
            [gd_scene format=3]

            [node name="Root" type="Control"]

            [node name="A2" type="Node" parent="." unique_id=7]
            """;

        string text = Forward(previous, tscn);

        Assert.Contains("<!-- about the second A -->\n  <Node Name=\"A2\" x:UniqueId=\"7\" />", text, StringComparison.Ordinal);
    }

    private const string Connection = """
        [connection signal="resized" from="." to="." method="OnResized"]
        """;

    // A scene whose root is a Control, with sections appended below it.
    private static string WithChild(string sections) => ControlRoot + "\n" + sections + "\n";

    // The text of the save that replaces the previous document: its .tscn converted forward with Previous, then written.
    // A comment the save cannot carry fails the test here, and so does any issue that is not one; a test that expects a
    // carried-comment warning converts for itself.
    private static string Forward(string previousText, string tscnText, XscnDocumentKind kind = XscnDocumentKind.Scene)
    {
        ConversionResult<XscnDocument> forward = Converted(previousText, tscnText, kind);
        Assert.DoesNotContain(forward.Issues, issue => issue.Code == ConversionCodes.CommentDropped);
        Assert.All(forward.Issues, issue => Assert.Equal(ConversionCodes.CommentDropped, issue.Code));
        return XscnWriter.Write(forward.Document, Metadata);
    }

    private static ConversionResult<XscnDocument> Converted(string previousText, string tscnText, XscnDocumentKind kind = XscnDocumentKind.Scene)
    {
        XscnParseResult parsed = XscnParser.Parse(previousText, kind, Metadata);
        Assert.False(parsed.HasErrors);
        return TscnToXscn.Convert(TscnReader.Read(tscnText), Metadata, Options with { Previous = parsed.Document });
    }

    // The one warning a save reports for a comment it could not carry.
    private static ConversionIssue Dropped(ConversionResult<XscnDocument> forward)
    {
        ConversionIssue issue = Assert.Single(forward.Issues, issue => issue.Code == ConversionCodes.CommentDropped);
        Assert.Equal(ConversionSeverity.Warning, issue.Severity);
        return issue;
    }
}
