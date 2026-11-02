using GodotXaml.Core.Tscn;

namespace GodotXaml.Core.Tests.Conversion;

public sealed class TscnTestNormalizerTests
{
    [Fact]
    public void NormalizerRenumbersIdsAndReferences()
    {
        const string text = """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://inner.tscn" id="1_abcde"]
            [ext_resource type="Texture2D" path="res://icon.png" id="2_fghij"]

            [sub_resource type="Gradient" id="Gradient_x1y2z"]

            [sub_resource type="GradientTexture2D" id="GradientTexture2D_q1w2e"]
            gradient = SubResource("Gradient_x1y2z")

            [node name="Root" type="Node2D"]

            [node name="Sprite" type="Sprite2D" parent="."]
            texture = SubResource("GradientTexture2D_q1w2e")
            metadata/list = [ExtResource("2_fghij"), SubResource("Gradient_x1y2z")]

            [node name="Inst" parent="." instance=ExtResource("1_abcde")]

            [connection signal="ready" from="Sprite" to="." method="set_meta" binds= ["icon", ExtResource("2_fghij")]]

            """;
        const string expected = """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://inner.tscn" id="1"]
            [ext_resource type="Texture2D" path="res://icon.png" id="2"]

            [sub_resource type="Gradient" id="Gradient_1"]

            [sub_resource type="GradientTexture2D" id="GradientTexture2D_2"]
            gradient = SubResource("Gradient_1")

            [node name="Root" type="Node2D"]

            [node name="Sprite" type="Sprite2D" parent="."]
            texture = SubResource("GradientTexture2D_2")
            metadata/list = [ExtResource("2"), SubResource("Gradient_1")]

            [node name="Inst" parent="." instance=ExtResource("1")]

            [connection signal="ready" from="Sprite" to="." method="set_meta" binds= ["icon", ExtResource("2")]]

            """;

        Assert.Equal(expected, TscnWriter.Write(TscnTestNormalizer.Normalize(TscnReader.Read(text))));
    }

    [Fact]
    public void NormalizerLeavesAnAlreadyNormalDocumentUnchanged()
    {
        const string text = """
            [gd_resource type="GradientTexture2D" format=3]

            [ext_resource type="Script" path="res://tagged.gd" id="1"]

            [sub_resource type="Gradient" id="Gradient_1"]

            [resource]
            gradient = SubResource("Gradient_1")
            metadata/script = ExtResource("1")

            """;
        TscnDocument document = TscnReader.Read(text);

        Assert.Equal(TscnWriter.Write(document), TscnWriter.Write(TscnTestNormalizer.Normalize(document)));
    }
}
