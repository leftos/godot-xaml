using GodotXaml.Core.Conversion;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// What a scene's .xscn cannot hold: each case is an Error issue, the item is not converted and the rest goes on.
public sealed partial class SceneForwardTests
{
    [Fact]
    public void NodeWhoseParentIsNoNodeIsNotConvertedWithItsSubtree()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [node name="Lost" type="Node" parent="Nowhere"]

            [node name="Below" type="Node" parent="Nowhere/Lost"]

            [node name="Kept" type="Node" parent="."]

            """
        );

        Assert.Equal([ConversionCodes.NodeParentNotFound, ConversionCodes.NodeParentNotFound], Codes(result));
        Assert.Equal("Kept", Assert.IsType<XscnNode>(Assert.Single(Root(result.Document).Children)).Name);
    }

    [Fact]
    public void InstanceThatIsNotAnExtResourceIsNotConverted()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [node name="Inst" parent="." instance="res://part.tscn"]

            """
        );

        Assert.Equal([ConversionCodes.InstanceNotAScene], Codes(result));
        Assert.Empty(Root(result.Document).Children);
    }

    [Fact]
    public void SecondSiblingWithTheSameNameIsNotConverted()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [node name="Twin" type="Node" parent="."]

            [node name="Twin" type="Node2D" parent="."]

            """
        );

        Assert.Equal([ConversionCodes.RepeatedSiblingName], Codes(result));
        XscnNode twin = Assert.IsType<XscnNode>(Assert.Single(Root(result.Document).Children));
        Assert.Equal("Node", Assert.IsType<XscnClassType>(twin.Type).Class.Name);
    }

    [Fact]
    public void ConnectionFromNoNodeIsNotConverted()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [node name="Plain" type="Node" parent="."]

            [connection signal="ready" from="Ghost" to="." method="go"]
            [connection signal="ready" from="Plain/Inner" to="." method="go"]

            """
        );

        Assert.Equal([ConversionCodes.ConnectionEmitterNotFound, ConversionCodes.ConnectionEmitterNotFound], Codes(result));
        Assert.Empty(Root(result.Document).Connections);
        Assert.Empty(Assert.Single(Root(result.Document).Children).Connections);
    }

    [Fact]
    public void EditableNamingNoNodeIsAnIssue()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [editable path="Ghost"]

            """
        );

        Assert.Equal([ConversionCodes.EditableNotFound], Codes(result));
        Assert.False(Root(result.Document).Editable);
    }

    [Fact]
    public void ExtraOnANodeAConnectionAndAnEditableIsAnIssue()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://part.tscn" id="1_p4rt0"]

            [node name="Root" type="Node"]

            [node name="Inst" parent="." instance=ExtResource("1_p4rt0") odd=1]

            [connection signal="ready" from="Inst" to="." method="go" odd=2]

            [editable path="Inst" odd=3]

            """
        );

        Assert.Equal(
            [ConversionCodes.UnrepresentableAttribute, ConversionCodes.UnrepresentableAttribute, ConversionCodes.UnrepresentableAttribute],
            Codes(result)
        );
        Assert.Equal(3, result.Issues.Select(issue => issue.Location).Distinct().Count());
        XscnNode instance = Assert.IsType<XscnNode>(Assert.Single(Root(result.Document).Children));
        Assert.True(instance.Editable);
        Assert.Single(instance.Connections);
    }

    [Fact]
    public void RootWithNeitherTypeNorSceneIsAStandIn()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            """
            [gd_scene format=3]

            [node name="Root"]

            [node name="Kept" type="Node" parent="."]

            """
        );

        Assert.Equal([ConversionCodes.RootNotConvertible], Codes(result));
        Assert.Equal("Node", Assert.IsType<XscnClassType>(Root(result.Document).Type).Class.Name);
        Assert.Single(Root(result.Document).Children);
    }

    [Fact]
    public void TypelessNodeOutsideAnyInstanceIsNotConverted()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [node name="Ghost" parent="."]

            """
        );

        Assert.Equal([ConversionCodes.NodeParentNotFound], Codes(result));
        Assert.Empty(Root(result.Document).Children);
    }

    [Fact]
    public void NodeWithNoParentAfterTheFirstIsNotConverted()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [node name="Second" type="Node"]

            """
        );

        Assert.Equal([ConversionCodes.NodeParentNotFound], Codes(result));
        Assert.Empty(Root(result.Document).Children);
    }

    [Fact]
    public void FailedInstanceTakesItsChildWithIt()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [node name="Inst" parent="." instance="res://part.tscn"]

            [node name="Kid" type="Node" parent="Inst"]

            """
        );

        Assert.Equal([ConversionCodes.InstanceNotAScene, ConversionCodes.NodeParentNotFound], Codes(result));
        Assert.Empty(Root(result.Document).Children);
    }

    // The reader refuses an id no section has, as Godot does, so the model is given the reference after reading.
    [Fact]
    public void InstanceOfAMissingExtResourceIsNotConverted()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            WithMissingInstance(
                """
                [gd_scene format=3]

                [node name="Root" type="Node"]

                [node name="Inst" parent="."]

                """,
                1
            )
        );

        Assert.Equal([ConversionCodes.UnknownExtResourceId, ConversionCodes.InstanceNotAScene], Codes(result));
        Assert.Empty(Root(result.Document).Children);
    }

    // The analysis reports the id itself (GXC0002); the root's own issue is GXC0010 alone, and its children are kept.
    [Fact]
    public void RootWithAMissingInstanceReportsOnce()
    {
        ConversionResult<XscnDocument> result = ConvertWithIssues(
            WithMissingInstance(
                """
                [gd_scene format=3]

                [node name="Root"]

                [node name="Kept" type="Node" parent="."]

                """,
                0
            )
        );

        Assert.Equal([ConversionCodes.UnknownExtResourceId, ConversionCodes.InstanceNotAScene], Codes(result));
        Assert.EndsWith("; a Node stands in for the root.", result.Issues[1].Message, StringComparison.Ordinal);
        Assert.Equal("Node", Assert.IsType<XscnClassType>(Root(result.Document).Type).Class.Name);
        Assert.Single(Root(result.Document).Children);
    }

    [Fact]
    public void InstanceAndPlaceholderIsAPlaceholderWithAWarning()
    {
        const string text = """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://part.tscn" id="1_p4rt0"]

            [node name="Root" type="Node"]

            [node name="Later" parent="." instance=ExtResource("1_p4rt0") instance_placeholder="res://later.tscn"]

            [node name="A" parent="Later"]

            """;

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(TscnReader.Read(text), Metadata, Options);

        Assert.Equal([ConversionCodes.InstanceAndPlaceholder, ConversionCodes.NodeParentNotFound], Codes(result));
        Assert.Equal(ConversionSeverity.Warning, result.Issues[0].Severity);
        XscnNode later = Assert.IsType<XscnNode>(Assert.Single(Root(result.Document).Children));
        Assert.Equal("InstancePlaceholder", Assert.IsType<XscnClassType>(later.Type).Class.Name);
        Assert.Equal("res://later.tscn", later.InstancePlaceholder);
        Assert.Empty(later.Children);
    }

    // Converts a scene whose issues are all Errors; returns the result for the codes and what was kept.
    private static ConversionResult<XscnDocument> ConvertWithIssues(string text) => ConvertWithIssues(TscnReader.Read(text));

    private static ConversionResult<XscnDocument> ConvertWithIssues(TscnDocument document)
    {
        ConversionResult<XscnDocument> result = TscnToXscn.Convert(document, Metadata, Options);
        Assert.All(result.Issues, issue => Assert.Equal(ConversionSeverity.Error, issue.Severity));
        return result;
    }

    // The scene with instance=ExtResource("missing") on one node.
    private static TscnDocument WithMissingInstance(string text, int node)
    {
        TscnDocument document = TscnReader.Read(text);
        var missing = new GodotResourceRef(GodotRefKind.External, "missing");
        return document with { Nodes = document.Nodes.SetItem(node, document.Nodes[node] with { Instance = missing }) };
    }

    private static string[] Codes(ConversionResult<XscnDocument> result) => [.. result.Issues.Select(issue => issue.Code)];
}
