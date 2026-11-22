using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// A save keeps the id state of the file it replaces: no x:Uids table when that file had none, and no x:UniqueId when none
// of its nodes had one, judged apart, though Godot's pack gives every node an id (docs/DECISIONS.md, "Canonical formatting
// on save"). Each test converts as the save route does: the two options set from the previous document by XscnIdState.
public sealed class PreviousIdStateTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    // The .tscn a headless save writes: an ext_resource with its uid, and a unique_id on every node.
    private const string SavedScene = """
        [gd_scene format=3]

        [ext_resource type="Texture2D" uid="uid://dframe" path="res://art/frame.png" id="1"]

        [node name="Root" type="Control" unique_id=11]

        [node name="Go" type="TextureRect" parent="." unique_id=12]
        texture = ExtResource("1")
        """;

    // The same scene, with an instance of a .tscn scene and an override inside it.
    private const string SavedInstance = """
        [gd_scene format=3]

        [ext_resource type="PackedScene" uid="uid://dpart" path="res://part.tscn" id="1"]

        [node name="Root" type="Control" unique_id=11]

        [node name="Part" parent="." instance=ExtResource("1") unique_id=12]

        [node name="Inner" parent="Part" unique_id=13]
        visible = false
        """;

    // An instance of a scene that has ids: an override two levels deep, a node added under its editable child, and a
    // connection from an inner node, each with the id paths Godot's pack builds from the ids it minted.
    private const string SavedIdPaths = """
        [gd_scene format=3]

        [ext_resource type="PackedScene" uid="uid://dpart" path="res://part.tscn" id="1"]

        [node name="Root" type="Control" unique_id=11]

        [node name="Part" parent="." instance=ExtResource("1") unique_id=12]

        [node name="Deep" parent="Part/Inner" parent_id_path=PackedInt32Array(12, 30) owner="Part" owner_uid_path=PackedInt32Array(12) unique_id=31]
        visible = false

        [node name="Added" type="Node" parent="Part/Inner" parent_id_path=PackedInt32Array(12, 30) unique_id=14]

        [connection signal="ready" from="Part/Inner" to="." method="go" from_uid_path=PackedInt32Array(12, 30) to_uid_path=PackedInt32Array(11)]

        [editable path="Part"]
        """;

    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly GodotMetadata Metadata48 = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.8.json"));

    [Fact]
    public void WritesNoTableAndNoIdsOverAFileWithNeither()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <TextureRect Name="Go" />
            </Control>
            """;

        string text = Save(previous, SavedScene);

        Assert.DoesNotContain("x:Uids", text, StringComparison.Ordinal);
        Assert.DoesNotContain("x:UniqueId", text, StringComparison.Ordinal);
        Assert.Contains("res://art/frame.png", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesTheTableAndNoIdsOverAFileWithATableAndNoIds()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <x:Uids>
                <x:Uid Path="res://art/frame.png" Value="uid://dframe" />
              </x:Uids>
              <TextureRect Name="Go" />
            </Control>
            """;

        string text = Save(previous, SavedScene);

        Assert.Contains("<x:Uid Path=\"res://art/frame.png\" Value=\"uid://dframe\" />", text, StringComparison.Ordinal);
        Assert.DoesNotContain("x:UniqueId", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesTheIdsAndNoTableOverAFileWithIdsAndNoTable()
    {
        string previous = $$"""
            <Control {{X}} Name="Root" x:UniqueId="11">
              <TextureRect Name="Go" x:UniqueId="12" />
            </Control>
            """;

        string text = Save(previous, SavedScene);

        Assert.DoesNotContain("x:Uids", text, StringComparison.Ordinal);
        Assert.Contains("x:UniqueId=\"11\"", text, StringComparison.Ordinal);
        Assert.Contains("x:UniqueId=\"12\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesAnIdOnEveryNodeOverAFileWhereSomeNodesHaveOne()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <TextureRect Name="Go" x:UniqueId="12" />
              <Node Name="Other" />
            </Control>
            """;

        string tscn = SavedScene + "\n\n[node name=\"Other\" type=\"Node\" parent=\".\" unique_id=13]\n";

        string text = Save(previous, tscn);

        Assert.Contains("x:UniqueId=\"11\"", text, StringComparison.Ordinal);
        Assert.Contains("x:UniqueId=\"12\"", text, StringComparison.Ordinal);
        Assert.Contains("x:UniqueId=\"13\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesTheTableAndTheIdsWithNoPreviousFile()
    {
        ConversionResult<XscnDocument> converted = TscnToXscn.Convert(TscnReader.Read(SavedScene), Metadata, new ConversionOptions());

        string text = XscnWriter.Write(converted.Document, Metadata);

        Assert.Empty(converted.Issues);
        Assert.Contains("<x:Uid Path=\"res://art/frame.png\" Value=\"uid://dframe\" />", text, StringComparison.Ordinal);
        Assert.Contains("x:UniqueId=\"11\"", text, StringComparison.Ordinal);
        Assert.Contains("x:UniqueId=\"12\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesBothByDefault()
    {
        var options = new ConversionOptions();

        Assert.True(options.WriteUidTable);
        Assert.True(options.WriteNodeIds);
    }

    [Fact]
    public void GivesANodeTheEditAddedNoIdInAFileWithNone()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <TextureRect Name="Go" />
            </Control>
            """;

        string tscn = SavedScene + "\n\n[node name=\"Added\" type=\"Node\" parent=\".\" unique_id=99]\n";

        string text = Save(previous, tscn);

        Assert.Contains("Name=\"Added\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("x:UniqueId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uids", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DropsTheCommentOfANodeRenamedInAFileWithNoIds()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about Go -->
              <TextureRect Name="Go" />
            </Control>
            """;

        ConversionResult<XscnDocument> forward = Saved(previous, SavedScene.Replace("name=\"Go\"", "name=\"Went\"", StringComparison.Ordinal));

        string text = XscnWriter.Write(forward.Document, Metadata);
        Assert.DoesNotContain("<!--", text, StringComparison.Ordinal);
        Assert.DoesNotContain("x:UniqueId", text, StringComparison.Ordinal);
        ConversionIssue issue = Assert.Single(forward.Issues, found => found.Code == ConversionCodes.CommentDropped);
        Assert.Equal("Go", issue.Location);
    }

    [Fact]
    public void CarriesTheCommentOfARenamedNodeByItsIdInAFileWithIds()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- about Go -->
              <TextureRect Name="Go" x:UniqueId="12" />
            </Control>
            """;

        string text = Save(previous, SavedScene.Replace("name=\"Go\"", "name=\"Went\"", StringComparison.Ordinal));

        Assert.Contains("<!-- about Go -->\n  <TextureRect Name=\"Went\"", text, StringComparison.Ordinal);
        Assert.Contains("x:UniqueId=\"11\"", text, StringComparison.Ordinal);
        Assert.Contains("x:UniqueId=\"12\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesAnInstanceOfATscnSceneAsXInstanceWithNoTable()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <x:Instance Name="Part" Scene="res://part.tscn">
                <x:Override Path="Inner" Visible="false" />
              </x:Instance>
            </Control>
            """;

        string text = Save(previous, SavedInstance);

        Assert.Contains("<x:Instance Name=\"Part\" Scene=\"res://part.tscn\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uids", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GivesAnOverrideNoIdInAFileWithNone()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <x:Instance Name="Part" Scene="res://part.tscn">
                <x:Override Path="Inner" Visible="false" />
              </x:Instance>
            </Control>
            """;

        string text = Save(previous, SavedInstance);

        Assert.Contains("<x:Override Path=\"Inner\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("x:UniqueId", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesNoIdPathsInAFileWithNoIds()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <x:Instance Name="Part" Scene="res://part.tscn" />
            </Control>
            """;

        string text = Save(previous, SavedIdPaths);

        Assert.Contains("Name=\"Added\"", text, StringComparison.Ordinal);
        Assert.Contains("Path=\"Inner/Deep\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IdPath", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UidPath", text, StringComparison.Ordinal);
        Assert.DoesNotContain("x:UniqueId", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsTheIdPathsInAFileWithIds()
    {
        string previous = $$"""
            <Control {{X}} Name="Root" x:UniqueId="11">
              <x:Instance Name="Part" Scene="res://part.tscn" />
            </Control>
            """;

        string text = Save(previous, SavedIdPaths);

        Assert.Equal(2, Occurrences(text, "x:ParentIdPath=\"12, 30\""));
        Assert.Contains("x:OwnerUidPath=\"12\"", text, StringComparison.Ordinal);
        Assert.Contains("FromUidPath=\"12, 30\"", text, StringComparison.Ordinal);
        Assert.Contains("ToUidPath=\"11\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CountsAnIdOnAnOverrideAsIds()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <x:Instance Name="Part" Scene="res://part.tscn">
                <x:Override Path="Inner" Visible="false" x:UniqueId="13" />
              </x:Instance>
            </Control>
            """;

        Assert.True(XscnIdState.HasNodeIds(Parsed(previous, XscnDocumentKind.Scene, Metadata)));
        string text = Save(previous, SavedInstance);
        Assert.Contains("x:UniqueId=\"11\"", text, StringComparison.Ordinal);
        Assert.Contains("x:UniqueId=\"12\"", text, StringComparison.Ordinal);
        Assert.Contains("x:UniqueId=\"13\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CountsAnIdInANestedSceneRootAsIds()
    {
        string previous = $$"""
            <Node3D {{X}} x:Godot="4.8" Name="Root">
              <x:Resources>
                <PackedScene x:Key="Inner">
                  <Node2D Name="InnerRoot">
                    <Node Name="Deep" x:UniqueId="5" />
                  </Node2D>
                </PackedScene>
              </x:Resources>
            </Node3D>
            """;

        Assert.True(XscnIdState.HasNodeIds(Parsed(previous, XscnDocumentKind.Scene, Metadata48)));
        Assert.False(
            XscnIdState.HasNodeIds(Parsed(previous.Replace(" x:UniqueId=\"5\"", "", StringComparison.Ordinal), XscnDocumentKind.Scene, Metadata48))
        );
    }

    [Fact]
    public void CountsAnIdInASceneInsideXSceneAsIds()
    {
        string previous = $$"""
            <Node3D {{X}} x:Godot="4.8" Name="Root">
              <x:Scene>
                <x:Scene.Metadata.inner>
                  <PackedScene>
                    <Node Name="R" x:UniqueId="5" />
                  </PackedScene>
                </x:Scene.Metadata.inner>
              </x:Scene>
            </Node3D>
            """;

        Assert.True(XscnIdState.HasNodeIds(Parsed(previous, XscnDocumentKind.Scene, Metadata48)));
    }

    [Fact]
    public void KeepsATableThatHoldsOnlyComments()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <!-- uids lead -->
              <x:Uids>
                <!-- uids trail -->
              </x:Uids>
              <TextureRect Name="Go" />
            </Control>
            """;

        Assert.True(XscnIdState.HasUidTable(Parsed(previous, XscnDocumentKind.Scene, Metadata)));
        string text = Save(previous, SavedScene);
        Assert.Contains("<!-- uids lead -->", text, StringComparison.Ordinal);
        Assert.Contains("<!-- uids trail -->", text, StringComparison.Ordinal);
        Assert.Contains("<x:Uid Path=\"res://art/frame.png\" Value=\"uid://dframe\" />", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CountsAnEmptyTableWithNoCommentsAsNoTable()
    {
        string previous = $$"""
            <Control {{X}} Name="Root">
              <x:Uids />
              <TextureRect Name="Go" />
            </Control>
            """;

        Assert.False(XscnIdState.HasUidTable(Parsed(previous, XscnDocumentKind.Scene, Metadata)));
        Assert.DoesNotContain("x:Uids", Save(previous, SavedScene), StringComparison.Ordinal);
    }

    [Fact]
    public void WritesNoTableOverAnXresWithNone()
    {
        string previous = $$"""
            <StyleBoxTexture {{X}} />
            """;

        string tres = """
            [gd_resource type="StyleBoxTexture" format=3]

            [ext_resource type="Texture2D" uid="uid://dframe" path="res://art/frame.png" id="1"]

            [resource]
            texture = ExtResource("1")
            """;

        string text = Save(previous, tres, XscnDocumentKind.Resource);

        Assert.DoesNotContain("x:Uids", text, StringComparison.Ordinal);
        Assert.Contains("res://art/frame.png", text, StringComparison.Ordinal);
    }

    // The text of the save that replaces the previous document; any issue fails the test.
    private static string Save(string previousText, string tscnText, XscnDocumentKind kind = XscnDocumentKind.Scene)
    {
        ConversionResult<XscnDocument> forward = Saved(previousText, tscnText, kind);
        Assert.Empty(forward.Issues);
        return XscnWriter.Write(forward.Document, Metadata);
    }

    // The forward conversion a save runs: Previous set, and the id state judged from it.
    private static ConversionResult<XscnDocument> Saved(string previousText, string tscnText, XscnDocumentKind kind = XscnDocumentKind.Scene)
    {
        XscnDocument previous = Parsed(previousText, kind, Metadata);
        var options = new ConversionOptions
        {
            Previous = new PreviousDocument(previous, Metadata),
            WriteUidTable = XscnIdState.HasUidTable(previous),
            WriteNodeIds = XscnIdState.HasNodeIds(previous),
        };
        return TscnToXscn.Convert(TscnReader.Read(tscnText), Metadata, options);
    }

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;

    private static XscnDocument Parsed(string text, XscnDocumentKind kind, GodotMetadata metadata)
    {
        XscnParseResult parsed = XscnParser.Parse(text, kind, metadata);
        Assert.False(parsed.HasErrors);
        return parsed.Document;
    }
}
