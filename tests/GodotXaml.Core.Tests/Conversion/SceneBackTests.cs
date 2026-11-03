using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// .xscn scenes to TscnDocument, written with TscnWriter and compared with what Godot 4.7.2 wrote.
public sealed partial class SceneBackTests
{
    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new() { Seed = 7 };

    public static TheoryData<string> Fixtures =>
        new(
            "animation_player_library",
            "bone_chain_settings",
            "connections",
            "ext_resource",
            "groups",
            "inherited",
            "instanced",
            "instanced_order",
            "lone_root",
            "multiline",
            "nested",
            "order_inner",
            "physics_bones",
            "placeholder",
            "placeholder_order",
            "property_order",
            "script_node",
            "skeleton_bones",
            "skeleton_modification_chains",
            "sub_resources",
            "two_bone_ik_modifiers",
            "unique_name"
        );

    // Ids normalised on both sides; property order is compared as Godot wrote it.
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ConvertsXscnToTheGodotScene(string fixture)
    {
        TscnDocument expected = TscnReader.Read(ReadFixture(fixture + ".tscn"));
        ConversionOptions options = Options with
        {
            ExtResourceTypes = new TypesOf(expected),
            SceneFiles = FixtureScenes.Instance,
            SourceKey = $"res://cases/{fixture}.xscn",
        };

        // A generated fixture with no Inputs entry is read from its golden.
        string input = Inputs.TryGetValue(fixture, out string? inline) ? inline : ResourceForwardTests.ReadFixture("converted", fixture + ".xscn");

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Parse(input), Metadata, options);

        Assert.Empty(back.Issues);
        Assert.Equal(TscnTestNormalizer.Comparable(expected), TscnTestNormalizer.Comparable(back.Document));
    }

    [Fact]
    public void InstanceIsANodeWithInstanceAndNoType()
    {
        TscnDocument scene = Back($$"""<Node {{X}} Name="Root"><x:Instance Name="Inst" Scene="res://inner.tscn" /></Node>""");

        TscnNode inst = scene.Nodes[1];
        Assert.Equal(("Inst", null, "."), (inst.Name, inst.Type, inst.Parent));
        Assert.Equal(new GodotResourceRef(GodotRefKind.External, ExtId(scene, "res://inner.tscn")), inst.Instance);
    }

    [Fact]
    public void InheritedRootHasInstanceAndNoType()
    {
        TscnDocument scene = Back($$"""<x:Instance {{X}} Name="Root" Scene="res://base.tscn"><Node Name="Extra" /></x:Instance>""");

        TscnNode root = scene.Nodes[0];
        Assert.Equal(("Root", null, null), (root.Name, root.Type, root.Parent));
        Assert.Equal(new GodotResourceRef(GodotRefKind.External, ExtId(scene, "res://base.tscn")), root.Instance);
        Assert.Equal(".", scene.Nodes[1].Parent);
    }

    [Fact]
    public void OverrideIsANodeWithParentPathThroughTheInstance()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <x:Instance Name="Inst" Scene="res://inner.tscn">
                <x:Override Path="A/B"><x:Prop Name="process_priority" Value="3" /></x:Override>
              </x:Instance>
            </Node>
            """
        );

        TscnNode node = scene.Nodes[2];
        Assert.Equal(("B", "Inst/A", null, null), (node.Name, node.Parent, node.Type, node.Instance));
        Assert.Equal(new TscnProperty("process_priority", new GodotInt(3)), Assert.Single(node.Properties));
    }

    [Fact]
    public void EmptyOverrideWritesNoSectionButItsChildrenUseItsPath()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <x:Instance Name="Inst" Scene="res://inner.tscn">
                <x:Override Path="A"><Node Name="Added" /></x:Override>
              </x:Instance>
            </Node>
            """
        );

        Assert.Equal([("Root", null), ("Inst", "."), ("Added", "Inst/A")], scene.Nodes.Select(node => (node.Name, node.Parent)));
    }

    [Fact]
    public void ConnectionOnAnOverrideIsFromItsPath()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <x:Instance Name="Inst" Scene="res://inner.tscn">
                <x:Override Path="A/B"><x:Connect Signal="ready" Method="on_ready" /></x:Override>
              </x:Instance>
            </Node>
            """
        );

        Assert.Equal(2, scene.Nodes.Length);
        TscnConnection connection = Assert.Single(scene.Connections);
        Assert.Equal(("ready", "Inst/A/B", ".", "on_ready"), (connection.Signal, connection.From, connection.To, connection.Method));
    }

    [Fact]
    public void UniqueWritesUniqueNameInOwnerFirst()
    {
        TscnDocument scene = Back($$"""<Node {{X}} Name="Root"><Node Name="%Child" EditorDescription="note" /></Node>""");

        Assert.Equal(["unique_name_in_owner", "editor_description"], scene.Nodes[1].Properties.Select(property => property.Name));
        Assert.Equal(new GodotBool(true), scene.Nodes[1].Properties[0].Value);
    }

    [Fact]
    public void EditableWritesAnEditableSectionInPreOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <Node Name="Box">
                <x:Instance Name="Deep" Scene="res://inner.tscn" x:Editable="True" />
              </Node>
              <x:Instance Name="Plain" Scene="res://inner.tscn" />
              <x:Instance Name="Last" Scene="res://inner.tscn" x:Editable="True" />
            </Node>
            """
        );

        Assert.Equal(["Box/Deep", "Last"], scene.Editables.Select(editable => editable.Path));
    }

    // The bind is on the first node, yet its file comes after every node's; the instance comes before the script.
    [Fact]
    public void ExtResourcesAreNumberedInDiscoveryOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <x:Connect Signal="ready" Method="set_meta" Binds='["icon", Resource("res://bind.png")]' />
              <x:Instance Name="Inst" Scene="res://inner.tscn" Script="res://inst.gd" />
              <Sprite2D Name="Sprite" Texture="res://sprite.png" />
            </Node>
            """
        );

        Assert.Equal(["res://inner.tscn", "res://inst.gd", "res://sprite.png", "res://bind.png"], scene.ExtResources.Select(ext => ext.Path));
        Assert.Equal(["1_", "2_", "3_", "4_"], scene.ExtResources.Select(ext => ext.Id[..2]));
    }

    // A node's files are numbered in the order its section writes its properties (packed_scene.cpp:860-866): Sprite2D's
    // own texture before script, though script sorts first.
    [Fact]
    public void ANodesFilesAreNumberedInItsPropertyListOrder()
    {
        TscnDocument scene = Back($$"""<Sprite2D {{X}} Name="S" Script="res://s.gd" Texture="res://t.png" />""");

        AssertExtOrder(scene, "res://t.png", "res://s.gd");
        Assert.Equal(["texture", "script"], Names(scene.Nodes[0].Properties));
    }

    // Control's theme overrides sit at Control's depth, before Button's own icon.
    [Fact]
    public void AThemeOverridesFileIsNumberedBeforeTheLeafClassesOwn()
    {
        TscnDocument scene = Back($$"""<Button {{X}} Name="B" Icon="res://i.png" ThemeOverrideFonts.font="res://f.ttf" />""");

        AssertExtOrder(scene, "res://f.ttf", "res://i.png");
        Assert.Equal(["theme_override_fonts/font", "icon"], Names(scene.Nodes[0].Properties));
    }

    // The class list, then script, then metadata (object.cpp:518-537).
    [Fact]
    public void AMetadataFileIsNumberedAfterTheScript()
    {
        TscnDocument scene = Back($$"""<Sprite2D {{X}} Name="S" Script="res://s.gd" Metadata.m='Resource("res://m.png")' Texture="res://t.png" />""");

        AssertExtOrder(scene, "res://t.png", "res://s.gd", "res://m.png");
        Assert.Equal(["texture", "script", "metadata/m"], Names(scene.Nodes[0].Properties));
    }

    // The instanced root is a Sprite2D: its own texture comes before script in Godot's list, though script sorts first.
    [Fact]
    public void AnInstancesFilesAreNumberedInItsRootClassesOrder()
    {
        var files = new MemoryScenes(("res://c.tscn", "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Sprite2D\"]\n"));

        ConversionResult<TscnDocument> back = BackWith(
            $$"""<Node {{X}} Name="Root"><x:Instance Name="I" Scene="res://c.tscn" Script="res://s.gd" Texture="res://t.png" /></Node>""",
            files
        );

        Assert.Empty(back.Issues);
        AssertExtOrder(back.Document, "res://c.tscn", "res://t.png", "res://s.gd");
        Assert.Equal(["texture", "script"], Names(back.Document.Nodes[1].Properties));
    }

    // Both sections instance one scene, so its uid is looked up for each; the disagreeing entries are warned about once.
    [Fact]
    public void TwoUidsOnAnInstancedScenePathWarnOnce()
    {
        var files = new MemoryScenes(("res://child.xscn", "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Node\"]\n"));

        ConversionResult<TscnDocument> back = BackWith(
            $$"""
            <Node {{X}} Name="Root">
              <x:Instance Name="A" Scene="res://child.xscn" ProcessPriority="1" ProcessPhysicsPriority="2" />
              <x:Instance Name="B" Scene="res://child.xscn" ProcessPriority="1" ProcessPhysicsPriority="2" />
              <x:Uids>
                <x:Uid Path="res://child.xscn" Value="uid://bbb" />
                <x:Uid Path="res://child.xscn" Value="uid://aaa" />
              </x:Uids>
            </Node>
            """,
            files
        );

        ConversionIssue issue = Assert.Single(back.Issues, issue => issue.Code == ConversionCodes.UidsDisagree);
        Assert.Equal(ConversionSeverity.Warning, issue.Severity);
        Assert.Contains("res://child.xscn", issue.Message, StringComparison.Ordinal);
        Assert.Equal("uid://bbb", Assert.Single(back.Document.ExtResources, ext => ext.Path == "res://child.xscn").Uid);
    }

    // A resource's references are found in sorted name order (resource_format_text.cpp:1680-1683), though it is written in
    // list order: BaseMaterial3D lists roughness_texture before emission_texture, which sorts first.
    [Fact]
    public void AnInlineResourcesFilesKeepSortedNameOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <MeshInstance3D {{X}} Name="M">
              <MeshInstance3D.MaterialOverride>
                <StandardMaterial3D RoughnessTexture="res://r.png" EmissionTexture="res://e.png" />
              </MeshInstance3D.MaterialOverride>
            </MeshInstance3D>
            """
        );

        AssertExtOrder(scene, "res://e.png", "res://r.png");
        Assert.Equal(["roughness_texture", "emission_texture"], Names(Assert.Single(scene.SubResources).Properties));
    }

    // With no class to order by (an unknown type, an instance with no lookup), the files follow the written order, as the
    // section's properties do.
    [Fact]
    public void ANodeWithNoKnownClassNumbersItsFilesInWrittenOrder()
    {
        XscnNode root = new()
        {
            Type = new XscnClassType(Metadata.FindClass("Node")!),
            Name = "Root",
            Children =
            [
                new XscnNode
                {
                    Type = new XscnUnknownType("MyThing"),
                    Name = "Unknown",
                    Properties = [new XscnProperty("script", new XscnFile("res://u.gd")), new XscnProperty("texture", new XscnFile("res://u.png"))],
                },
                new XscnNode
                {
                    Type = new XscnSceneType("res://c.tscn"),
                    Name = "Inst",
                    Properties = [new XscnProperty("texture", new XscnFile("res://i.png")), new XscnProperty("script", new XscnFile("res://i.gd"))],
                },
            ],
        };

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(new XscnDocument(XscnDocumentKind.Scene, [], [], [], root), Metadata, Options);

        AssertExtOrder(back.Document, "res://u.gd", "res://u.png", "res://c.tscn", "res://i.png", "res://i.gd");
        Assert.Equal(["script", "texture"], Names(back.Document.Nodes[1].Properties));
        Assert.Equal(["texture", "script"], Names(back.Document.Nodes[2].Properties));
    }

    // A script export has no declared type, so only the extension says what the file holds.
    [Fact]
    public void AGdshaderFileComesBackAsAShader()
    {
        TscnDocument scene = Back($$"""<Node {{X}} Name="Root" shader='Resource("res://s.gdshader")' />""");

        Assert.Equal("Shader", Assert.Single(scene.ExtResources).Type);
    }

    [Fact]
    public void AGdshaderincFileComesBackAsAShaderInclude()
    {
        TscnDocument scene = Back($$"""<Node {{X}} Name="Root" shader='Resource("res://s.gdshaderinc")' />""");

        Assert.Equal("ShaderInclude", Assert.Single(scene.ExtResources).Type);
    }

    // The extension says nothing, so only the instance's declared type gives PackedScene.
    [Fact]
    public void InstanceExtResourceTypeIsPackedScene()
    {
        TscnDocument scene = Back($$"""<Node {{X}} Name="Root"><x:Instance Name="Inst" Scene="res://inner.scene" /></Node>""");

        Assert.Equal("PackedScene", Assert.Single(scene.ExtResources).Type);
    }

    [Fact]
    public void HeaderUidComesFromTheOption()
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(
            Parse($$"""<Node {{X}} Name="Root" />"""),
            Metadata,
            Options with
            {
                Uid = "uid://b1x2y3",
            }
        );

        Assert.Empty(back.Issues);
        Assert.Equal("uid://b1x2y3", back.Document.Header.Uid);
        Assert.StartsWith("[gd_scene format=3 uid=\"uid://b1x2y3\"]\n", TscnWriter.Write(back.Document), StringComparison.Ordinal);
    }

    [Fact]
    public void KeepIdsReusesRecordedIdsForAScene()
    {
        XscnDocument document = Parse(
            $$"""
            <Node {{X}} Name="Root">
              <x:Instance Name="Inst" Scene="res://inner.tscn" />
              <Sprite2D Name="Sprite">
                <Sprite2D.Material><CanvasItemMaterial /></Sprite2D.Material>
              </Sprite2D>
            </Node>
            """
        );
        XscnNode root = Assert.IsType<XscnNode>(document.Root);
        XscnResource material = Assert.IsType<XscnInline>(Assert.Single(root.Children[1].Properties).Value).Resource;
        var ids = new ConversionIds(
            new Dictionary<string, string> { ["res://inner.tscn"] = "1_keep1" },
            new Dictionary<XscnResource, string>(ReferenceEqualityComparer.Instance) { [material] = "CanvasItemMaterial_keep1" }
        );

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, Metadata, Options with { KeepIds = ids });

        Assert.Empty(back.Issues);
        Assert.Equal("1_keep1", Assert.Single(back.Document.ExtResources).Id);
        Assert.Equal("CanvasItemMaterial_keep1", Assert.Single(back.Document.SubResources).Id);
        Assert.Equal(new GodotResourceRef(GodotRefKind.External, "1_keep1"), back.Document.Nodes[1].Instance);
        Assert.Equal(new GodotResourceRef(GodotRefKind.Sub, "CanvasItemMaterial_keep1"), Assert.Single(back.Document.Nodes[2].Properties).Value);
    }

    [Fact]
    public void SceneWhoseRootIsAResourceIsReported()
    {
        var document = new XscnDocument(
            XscnDocumentKind.Scene,
            [],
            [],
            [],
            new XscnResource { Type = new XscnClassType(Metadata.FindClass("Gradient")!) }
        );

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, Metadata, Options);

        AssertIssue(back.Issues, ConversionCodes.SceneRootNotANode, ConversionSeverity.Error);
        Assert.Empty(back.Document.Nodes);
    }

    // Under a node of the scene's own, and under another override, an override has no instance to be a path into.
    [Fact]
    public void OverrideOutsideAnInstanceIsReported()
    {
        XscnOverride inner = new() { Path = "C", Properties = [new XscnProperty("process_priority", new XscnScalar(new GodotInt(1)))] };
        XscnOverride outer = new() { Path = "B", Children = [inner] };
        XscnNode root = new()
        {
            Type = new XscnClassType(Metadata.FindClass("Node")!),
            Name = "Root",
            Children =
            [
                new XscnNode
                {
                    Type = new XscnSceneType("res://inner.tscn"),
                    Name = "Inst",
                    Children = [outer],
                },
                new XscnOverride { Path = "A" },
            ],
        };

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(new XscnDocument(XscnDocumentKind.Scene, [], [], [], root), Metadata, Options);

        Assert.Equal(
            2,
            back.Issues.Count(issue => issue.Code == ConversionCodes.OverrideOutsideInstance && issue.Severity == ConversionSeverity.Error)
        );
        Assert.Equal(("C", "Inst/B"), (back.Document.Nodes[^1].Name, back.Document.Nodes[^1].Parent));
    }

    [Fact]
    public void OverrideWithoutAPathIsReportedAndNotWritten()
    {
        XscnNode root = new()
        {
            Type = new XscnClassType(Metadata.FindClass("Node")!),
            Name = "Root",
            Children =
            [
                new XscnNode
                {
                    Type = new XscnSceneType("res://inner.tscn"),
                    Name = "Inst",
                    Children =
                    [
                        new XscnOverride
                        {
                            Path = "",
                            Children = [new XscnNode { Type = new XscnClassType(Metadata.FindClass("Node")!), Name = "Lost" }],
                        },
                    ],
                },
            ],
        };

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(new XscnDocument(XscnDocumentKind.Scene, [], [], [], root), Metadata, Options);

        AssertIssue(back.Issues, ConversionCodes.OverrideWithoutPath, ConversionSeverity.Error);
        Assert.Equal(["Root", "Inst"], back.Document.Nodes.Select(node => node.Name));
    }

    [Theory]
    [InlineData("A/")]
    [InlineData("./A")]
    [InlineData("A//B")]
    [InlineData("..")]
    public void OverrideWithABadPathSegmentIsReported(string path)
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(
            Parse(
                $$"""
                <Node {{X}} Name="Root">
                  <x:Instance Name="Inst" Scene="res://inner.tscn">
                    <x:Override Path="{{path}}" x:Index="0"><Node Name="Lost" /></x:Override>
                  </x:Instance>
                </Node>
                """
            ),
            Metadata,
            Options
        );

        AssertIssue(back.Issues, ConversionCodes.OverrideWithoutPath, ConversionSeverity.Error);
        Assert.Equal(["Root", "Inst"], back.Document.Nodes.Select(node => node.Name));
    }

    [Fact]
    public void UniqueAndAnExplicitUniqueNameInOwnerWriteItOnce()
    {
        TscnDocument scene = Back($$"""<Node {{X}} Name="Root"><Node Name="%Child" UniqueNameInOwner="True" /></Node>""");

        Assert.Equal(new TscnProperty("unique_name_in_owner", new GodotBool(true)), Assert.Single(scene.Nodes[1].Properties));
    }

    [Fact]
    public void UniqueAndUniqueNameInOwnerFalseWarns()
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(
            Parse($$"""<Node {{X}} Name="Root"><Node Name="%Child" UniqueNameInOwner="False" /></Node>"""),
            Metadata,
            Options
        );

        AssertIssue(back.Issues, ConversionCodes.UniqueNameConflict, ConversionSeverity.Warning);
        Assert.Equal(new TscnProperty("unique_name_in_owner", new GodotBool(true)), Assert.Single(back.Document.Nodes[1].Properties));
    }

    // The tag names a class or a scene, which a placeholder's [node] has no place for.
    [Theory]
    [InlineData("""<Node2D Name="P" x:InstancePlaceholder="res://a.tscn" />""")]
    [InlineData("""<x:Instance Name="P" Scene="res://b.tscn" x:InstancePlaceholder="res://a.tscn" />""")]
    public void PlaceholderWithAnotherTagWarns(string element)
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Parse($$"""<Node {{X}} Name="Root">{{element}}</Node>"""), Metadata, Options);

        AssertIssue(back.Issues, ConversionCodes.PlaceholderTypeDropped, ConversionSeverity.Warning);
        TscnNode node = back.Document.Nodes[1];
        Assert.Equal(("P", null, null, "res://a.tscn"), (node.Name, node.Type, node.Instance, node.InstancePlaceholder));
        Assert.Empty(back.Document.ExtResources);
    }

    [Fact]
    public void PropertiesComeBackInGodotsPropertyListOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <Control {{X}} Name="Root">
              <Panel Name="Box" AnchorBottom="1" AnchorRight="1" AnchorsPreset="FullRect" LayoutMode="Anchors" />
              <Button Name="Toggle" ButtonPressed="True" ToggleMode="True" />
            </Control>
            """
        );

        Assert.Equal(["layout_mode", "anchors_preset", "anchor_right", "anchor_bottom"], Names(scene.Nodes[1].Properties));
        Assert.Equal(["toggle_mode", "button_pressed"], Names(scene.Nodes[2].Properties));
    }

    [Fact]
    public void ABaseClassPropertyComesBeforeADerivedOne()
    {
        TscnDocument scene = Back($$"""<Label {{X}} Name="Root" Text="Hi" Visible="False" LayoutMode="Position" />""");

        Assert.Equal(["visible", "layout_mode", "text"], Names(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ThemeOverridesComeAfterControlsOwnList()
    {
        TscnDocument scene = Back($$"""<Label {{X}} Name="Root" Text="Hi" ThemeOverrideColors.font_color="1, 0, 0, 1" LayoutMode="Position" />""");

        Assert.Equal(["layout_mode", "theme_override_colors/font_color", "text"], Names(scene.Nodes[0].Properties));
    }

    // Label's list puts font_size before font_color, against the alphabetical order of their families.
    [Fact]
    public void ThemeOverridesComeInTheMetadatasOrder()
    {
        TscnDocument scene = Back(
            $$"""<Label {{X}} Name="Root" ThemeOverrideColors.font_color="1, 0, 0, 1" ThemeOverrideFontSizes.font_size="20" />"""
        );

        Assert.Equal(["theme_override_font_sizes/font_size", "theme_override_colors/font_color"], Names(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ScriptThenExportsThenMetadataComeLast()
    {
        TscnDocument scene = Back($$"""<Node2D {{X}} Name="Root" Metadata.m="1" alpha="2" Script="res://s.gd" Position="1, 2" />""");

        Assert.Equal(["position", "script", "alpha", "metadata/m"], Names(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ADynamicNameComesAfterTheClassList()
    {
        TscnDocument scene = Back(
            $$"""
            <PopupMenu {{X}} Name="Root">
              <x:Prop Name="item_0/text" Value='"One"' />
              <x:Prop Name="item_count" Value="1" />
            </PopupMenu>
            """
        );

        Assert.Equal(["item_count", "item_0/text"], Names(scene.Nodes[0].Properties));
    }

    // The script's own _get_property_list gives such a name, and the node has no script until the script line is set.
    [Fact]
    public void ASlashNamedScriptPropertyComesAfterTheScript()
    {
        TscnDocument scene = Back(
            $$"""
            <Node2D {{X}} Name="Root" Script="res://s.gd">
              <x:Prop Name="settings/speed" Value="2" />
            </Node2D>
            """
        );

        Assert.Equal(["script", "settings/speed"], Names(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ASubResourceComesBackInOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <PanelContainer {{X}} Name="Root">
              <PanelContainer.ThemeOverrideStyles.panel>
                <StyleBoxFlat BgColor="1, 0, 0, 1" ContentMarginLeft="4" />
              </PanelContainer.ThemeOverrideStyles.panel>
            </PanelContainer>
            """
        );

        Assert.Equal(["content_margin_left", "bg_color"], Names(Assert.Single(scene.SubResources).Properties));
    }

    [Fact]
    public void AResourceRootComesBackInOrder()
    {
        XscnParseResult parsed = XscnParser.Parse(
            $$"""<StyleBoxFlat {{X}} BgColor="1, 0, 0, 1" ContentMarginLeft="4" />""",
            XscnDocumentKind.Resource,
            Metadata
        );
        Assert.Empty(parsed.Diagnostics);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, Metadata, Options);

        Assert.Empty(back.Issues);
        Assert.Equal(["content_margin_left", "bg_color"], Names(back.Document.ResourceProperties));
    }

    [Fact]
    public void AnInstanceAnOverrideAndAnUnknownClassKeepSourceOrder()
    {
        ImmutableArray<XscnProperty> alphabetical =
        [
            new XscnProperty("button_pressed", new XscnScalar(new GodotBool(true))),
            new XscnProperty("toggle_mode", new XscnScalar(new GodotBool(true))),
        ];
        XscnNode root = new()
        {
            Type = new XscnClassType(Metadata.FindClass("Node")!),
            Name = "Root",
            Children =
            [
                new XscnNode
                {
                    Type = new XscnSceneType("res://inner.tscn"),
                    Name = "Inst",
                    Properties = alphabetical,
                    Children = [new XscnOverride { Path = "A", Properties = alphabetical }],
                },
                new XscnNode
                {
                    Type = new XscnUnknownType("MyThing"),
                    Name = "Unknown",
                    Properties = alphabetical,
                },
            ],
        };

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(new XscnDocument(XscnDocumentKind.Scene, [], [], [], root), Metadata, Options);

        Assert.Empty(back.Issues);
        Assert.Equal(["Inst", "A", "Unknown"], back.Document.Nodes.Skip(1).Select(node => node.Name));
        Assert.All(back.Document.Nodes.Skip(1), node => Assert.Equal(["button_pressed", "toggle_mode"], Names(node.Properties)));
    }

    // With no lookup the placeholder's scene cannot be read, so its stored values keep source order: Node lists
    // process_mode first, and the tag's class does not order them.
    [Fact]
    public void APlaceholderKeepsSourceOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <InstancePlaceholder Name="Later" x:InstancePlaceholder="res://a.tscn" EditorDescription="note" ProcessMode="Always" />
            </Node>
            """
        );

        Assert.Equal(["editor_description", "process_mode"], Names(scene.Nodes[1].Properties));
    }

    // A placeholder replays its values in file order on the instanced root (instance_placeholder.cpp:37-43, 100-102), so
    // the root's class orders them: Control lists layout_mode before grow_vertical.
    [Fact]
    public void APlaceholderTakesTheRootClassOfItsScene()
    {
        var files = new MemoryScenes(("res://c.tscn", "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Control\"]\n"));

        ConversionResult<TscnDocument> back = BackWith(Placeholder("grow_vertical=\"0\" layout_mode=\"1\""), files);

        Assert.Empty(back.Issues);
        Assert.Equal(["layout_mode", "grow_vertical"], Names(back.Document.Nodes[1].Properties));
    }

    [Fact]
    public void APlaceholderWithNoLookupKeepsSourceOrderSilently()
    {
        TscnDocument scene = Back(Placeholder("grow_vertical=\"0\" layout_mode=\"1\""));

        Assert.Equal(["grow_vertical", "layout_mode"], Names(scene.Nodes[1].Properties));
    }

    [Fact]
    public void APlaceholderWhoseSceneCannotBeReadKeepsSourceOrderAndWarns()
    {
        var files = new MemoryScenes();

        ConversionResult<TscnDocument> back = BackWith(Placeholder("grow_vertical=\"0\" layout_mode=\"1\""), files);

        ConversionIssue issue = Assert.Single(back.Issues);
        Assert.Equal(
            (ConversionCodes.InstancedClassUnreadable, ConversionSeverity.Warning, "node \"P\""),
            (issue.Code, issue.Severity, issue.Location)
        );
        Assert.Equal(["grow_vertical", "layout_mode"], Names(back.Document.Nodes[1].Properties));
    }

    // Node lists process_mode before editor_description.
    [Fact]
    public void APlaceholderOfANodeSceneTakesNodesOrder()
    {
        var files = new MemoryScenes(("res://c.tscn", "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Node\"]\n"));

        ConversionResult<TscnDocument> back = BackWith(Placeholder("EditorDescription=\"note\" ProcessMode=\"Always\""), files);

        Assert.Empty(back.Issues);
        Assert.Equal(["process_mode", "editor_description"], Names(back.Document.Nodes[1].Properties));
    }

    [Fact]
    public void APlaceholderWithOnePropertyDoesNotRead()
    {
        var files = new MemoryScenes(("res://c.tscn", "[gd_scene format=3]\n\n[node name=\"Root\" type=\"Control\"]\n"));

        ConversionResult<TscnDocument> back = BackWith(Placeholder("layout_mode=\"1\""), files);

        Assert.Empty(back.Issues);
        Assert.Equal(0, files.Reads);
    }

    [Fact]
    public void RepeatedNamesKeepTheirOrder()
    {
        XscnNode root = new()
        {
            Type = new XscnClassType(Metadata.FindClass("Node")!),
            Name = "Root",
            Properties =
            [
                new XscnProperty("alpha", new XscnScalar(new GodotInt(1))),
                new XscnProperty("process_priority", new XscnScalar(new GodotInt(2))),
                new XscnProperty("alpha", new XscnScalar(new GodotInt(3))),
            ],
        };

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(new XscnDocument(XscnDocumentKind.Scene, [], [], [], root), Metadata, Options);

        Assert.Empty(back.Issues);
        Assert.Equal(
            [
                new TscnProperty("process_priority", new GodotInt(2)),
                new TscnProperty("alpha", new GodotInt(1)),
                new TscnProperty("alpha", new GodotInt(3)),
            ],
            back.Document.Nodes[0].Properties
        );
    }

    private static IEnumerable<string> Names(ImmutableArray<TscnProperty> properties) => properties.Select(property => property.Name);

    private static XscnDocument Parse(string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata);
        Assert.Empty(parsed.Diagnostics);
        return parsed.Document;
    }

    // Parses and converts with no issue.
    private static TscnDocument Back(string text)
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Parse(text), Metadata, Options);
        Assert.Empty(back.Issues);
        return back.Document;
    }

    // Parses and converts with a scene lookup, keeping the issues for the caller.
    private static ConversionResult<TscnDocument> BackWith(string text, ISceneFiles files) =>
        XscnToTscn.Convert(Parse(text), Metadata, Options with { SceneFiles = files, SourceKey = "res://main.xscn" });

    private static string Placeholder(string properties) =>
        $$"""<Node {{X}} Name="Root"><InstancePlaceholder Name="P" x:InstancePlaceholder="res://c.tscn" {{properties}} /></Node>""";

    // The files in discovery order: numbered 1_, 2_, ... in that order.
    private static void AssertExtOrder(TscnDocument scene, params string[] paths)
    {
        TscnExtResource[] byNumber = [.. scene.ExtResources.OrderBy(ext => int.Parse(ext.Id[..ext.Id.IndexOf('_')], CultureInfo.InvariantCulture))];
        Assert.Equal(paths, byNumber.Select(ext => ext.Path));
        Assert.Equal(Enumerable.Range(1, paths.Length).Select(n => $"{n}_"), byNumber.Select(ext => ext.Id[..(ext.Id.IndexOf('_') + 1)]));
    }

    private static string ExtId(TscnDocument scene, string path) => Assert.Single(scene.ExtResources, ext => ext.Path == path).Id;

    private static void AssertIssue(ImmutableArray<ConversionIssue> issues, string code, ConversionSeverity severity) =>
        Assert.Equal(severity, Assert.Single(issues, issue => issue.Code == code).Severity);

    private static string ReadFixture(string file) =>
        new UTF8Encoding(false, true).GetString(File.ReadAllBytes(RepoFiles.PathOf("tests", "fixtures", "scenes", file)));

    // The fixture's own ext_resource types, so the lookup is exact.
    private sealed class TypesOf(TscnDocument source) : IExtResourceTypes
    {
        public string? ClassOf(string path) => source.ExtResources.FirstOrDefault(ext => ext.Path == path)?.Type;
    }

    // The scene fixtures at the res://cases/ paths they instance each other by, as the generator saved them.
    internal sealed class FixtureScenes : ISceneFiles
    {
        private const string Cases = "res://cases/";

        public static FixtureScenes Instance { get; } = new();

        public SceneFile? Read(string referrer, string reference, string? uid) =>
            reference.StartsWith(Cases, StringComparison.Ordinal)
            && File.Exists(RepoFiles.PathOf("tests", "fixtures", "scenes", reference[Cases.Length..]))
                ? new SceneFile(reference, ReadFixture(reference[Cases.Length..]))
                : null;
    }

    // Scenes held in memory by their res:// path; counts the reads.
    private sealed class MemoryScenes(params (string Path, string Text)[] scenes) : ISceneFiles
    {
        public int Reads { get; private set; }

        public SceneFile? Read(string referrer, string reference, string? uid)
        {
            Reads++;
            return scenes.Where(scene => scene.Path == reference).Select(scene => new SceneFile(scene.Path, scene.Text)).FirstOrDefault();
        }
    }
}
