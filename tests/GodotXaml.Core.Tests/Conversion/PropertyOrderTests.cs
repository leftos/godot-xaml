using System.Collections.Immutable;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// A dynamic name (one a class adds in _get_property_list, which ClassDB does not list) goes in its declaring class's place.
public sealed class PropertyOrderTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new() { Seed = 7 };

    // AnimationMixer declares libraries/, so a library exists before AnimationPlayer's current_animation plays it.
    [Fact]
    public void ABaseClassFamilyComesBeforeTheLeafClassList()
    {
        TscnDocument scene = Back(
            $$"""
            <AnimationPlayer {{X}} Name="Root" CurrentAnimation="idle">
              <x:Prop Name="libraries/lib" Value='Resource("res://l.tres")' />
            </AnimationPlayer>
            """
        );

        Assert.Equal(["libraries/lib", "current_animation"], Names(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ALeafFamilyWithNoSlashComesBeforeTheScript()
    {
        TscnDocument scene = Back(
            $$"""
            <AnimationPlayer {{X}} Name="Root" Script="res://s.gd" SpeedScale="2">
              <x:Prop Name="blend_times" Value="[]" />
            </AnimationPlayer>
            """
        );

        Assert.Equal(["speed_scale", "blend_times", "script"], Names(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ALoneDynamicNameComesBeforeTheScript()
    {
        TscnDocument scene = Back(
            $$"""
            <AudioListener3D {{X}} Name="Root" Script="res://s.gd">
              <x:Prop Name="current" Value="true" />
            </AudioListener3D>
            """
        );

        Assert.Equal(["current", "script"], Names(scene.Nodes[0].Properties));
    }

    // GeometryInstance3D, a base of MeshInstance3D, declares instance_shader_parameters/, so it precedes mesh.
    [Fact]
    public void FamiliesFollowTheirDeclaringClassesInTheChain()
    {
        TscnDocument scene = Back(
            $$"""
            <MeshInstance3D {{X}} Name="Root" Mesh="res://m.tres">
              <x:Prop Name="surface_material_override/0" Value='Resource("res://a.tres")' />
              <x:Prop Name="instance_shader_parameters/p" Value="1.0" />
            </MeshInstance3D>
            """
        );

        Assert.Equal(["instance_shader_parameters/p", "mesh", "surface_material_override/0"], Names(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ACanvasItemFamilyComesBeforeTheSpriteList()
    {
        TscnDocument scene = Back(
            $$"""
            <Sprite2D {{X}} Name="Root" Texture="res://t.png">
              <x:Prop Name="instance_shader_parameters/p" Value="1.0" />
            </Sprite2D>
            """
        );

        Assert.Equal(["instance_shader_parameters/p", "texture"], Names(scene.Nodes[0].Properties));
    }

    // Node2D does not inherit AnimationMixer, so libraries/ there is a script's own name.
    [Fact]
    public void AFamilyOfAClassOutsideTheChainStaysAfterTheScript()
    {
        TscnDocument scene = Back(
            $$"""
            <Node2D {{X}} Name="Root" Script="res://s.gd">
              <x:Prop Name="libraries/x" Value="1" />
            </Node2D>
            """
        );

        Assert.Equal(["script", "libraries/x"], Names(scene.Nodes[0].Properties));
    }

    // Material lists render_priority, ShaderMaterial lists shader, then ShaderMaterial adds shader_parameter/.
    [Fact]
    public void AResourceFamilyComesAfterItsClassList()
    {
        TscnDocument scene = Back(
            $$"""
            <MeshInstance3D {{X}} Name="Root">
              <MeshInstance3D.MaterialOverride>
                <ShaderMaterial Shader="res://s.gdshader" RenderPriority="1" Script="res://m.gd">
                  <x:Prop Name="shader_parameter/c" Value="1.0" />
                </ShaderMaterial>
              </MeshInstance3D.MaterialOverride>
            </MeshInstance3D>
            """
        );

        Assert.Equal(["render_priority", "shader", "shader_parameter/c", "script"], Names(Assert.Single(scene.SubResources).Properties));
    }

    [Fact]
    public void MetadataAndExportsStayLast()
    {
        TscnDocument scene = Back(
            $$"""
            <AudioListener3D {{X}} Name="Root" Script="res://s.gd" Metadata.m="1" alpha="2">
              <x:Prop Name="current" Value="true" />
            </AudioListener3D>
            """
        );

        Assert.Equal(["current", "script", "alpha", "metadata/m"], Names(scene.Nodes[0].Properties));
    }

    private static IEnumerable<string> Names(ImmutableArray<TscnProperty> properties) => properties.Select(property => property.Name);

    // Parses and converts with no issue.
    private static TscnDocument Back(string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata);
        Assert.Empty(parsed.Diagnostics);
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, Metadata, Options);
        Assert.Empty(back.Issues);
        return back.Document;
    }
}
