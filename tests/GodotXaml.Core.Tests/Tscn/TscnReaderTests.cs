using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Tscn;

public sealed class TscnReaderTests
{
    [Fact]
    public void ReadsAnInstancedSceneWithAnOverrideAnIndexAndAnEditablePath()
    {
        TscnDocument document = Fixture("instanced.tscn");

        Assert.Equal(TscnKind.Scene, document.Kind);
        TscnExtResource ext = Assert.Single(document.ExtResources);
        Assert.Equal(("PackedScene", "res://cases/nested.tscn", "1_n3st3", (string?)null), (ext.Type, ext.Path, ext.Id, ext.Uid));
        TscnNode instance = document.Nodes[1];
        Assert.Equal(("Inst", null, "."), (instance.Name, instance.Type, instance.Parent));
        Assert.Equal(new GodotResourceRef(GodotRefKind.External, "1_n3st3"), instance.Instance);
        Assert.Equal(new TscnProperty("process_priority", new GodotInt(7)), Assert.Single(instance.Properties));
        Assert.Equal((0, (int?)null), (document.Nodes[2].Index, document.Nodes[0].Index));
        Assert.Equal(1, document.Nodes[3].Index);
        Assert.Equal("Inst", Assert.Single(document.Editables).Path);
    }

    [Fact]
    public void ReadsConnectionFlagsUnbindsAndBinds()
    {
        TscnDocument document = Fixture("connections.tscn");

        Assert.Equal([1, 0, 0, 0, 0], document.Connections.Select(connection => connection.Unbinds));
        Assert.Equal([2, 6, 2, 2, 3], document.Connections.Select(connection => connection.Flags));
        Assert.Equal([new GodotBool(true)], document.Connections[3].Binds);
        Assert.Equal(
            ("tree_entered", "Emitter", "Receiver", "set_process"),
            (document.Connections[3].Signal, document.Connections[3].From, document.Connections[3].To, document.Connections[3].Method)
        );
    }

    [Fact]
    public void ReadsGroupsNodePathsUniqueIdsAndAnInstancePlaceholder()
    {
        Assert.Equal(["alpha", "zeta"], Fixture("groups.tscn").Nodes[1].Groups);
        TscnNode holder = Fixture("script_node.tscn").Nodes[1];
        Assert.Equal(["target"], holder.NodePaths);
        Assert.NotNull(holder.UniqueId);
        Assert.Equal("res://cases/lone_root.tscn", Fixture("placeholder.tscn").Nodes[1].InstancePlaceholder);
    }

    [Fact]
    public void ReadsAResourceHeaderAndItsProperties()
    {
        TscnDocument tagged = Fixture("script_class.tres");
        Assert.Equal(TscnKind.Resource, tagged.Kind);
        Assert.Equal(("Resource", "TaggedResource", (int?)3), (tagged.Header.Type, tagged.Header.ScriptClass, tagged.Header.Format));
        Assert.Equal(new TscnProperty("label", new GodotString("tagged")), tagged.ResourceProperties[1]);
        Assert.Equal(4, Fixture("packed_bytes.tres").Header.Format);
        Assert.Equal(
            ["with space", "key=value", "say \"hi\"", "naïve"],
            Fixture("quoted_name.tres").ResourceProperties.Skip(1).Select(property => property.Name)
        );
    }

    [Fact]
    public void ReadsSubResourcesInOrderWithTheirProperties()
    {
        TscnDocument document = Fixture("sub_resources.tscn");
        Assert.Equal(["Gradient_gr4d1", "GradientTexture2D_0n3us", "CanvasItemMaterial_sh4r3"], document.SubResources.Select(sub => sub.Id));
        Assert.Equal(new GodotResourceRef(GodotRefKind.Sub, "Gradient_gr4d1"), document.SubResources[1].Properties[0].Value);
    }

    // 4.7.2 does not write load_steps (resource_format_text.cpp:1800-1819), so it is read and dropped.
    [Fact]
    public void DropsLoadSteps()
    {
        TscnDocument document = TscnReader.Read("[gd_scene load_steps=3 format=3]\n\n[node name=\"R\" type=\"Node\"]\n");
        Assert.Empty(document.Header.Extra);
        Assert.Equal("[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\n", TscnWriter.Write(document));
    }

    [Fact]
    public void KeepsUnknownAttributesAndWritesThemAfterTheKnownOnes()
    {
        const string Text =
            "[gd_scene format=3 future=true]\n\n" + "[node name=\"R\" future_attr=Vector2(1,   2) type=\"Node\" other=\"x\"]\n" + "a = 1\n";

        TscnDocument document = TscnReader.Read(Text);

        Assert.Equal([new TscnField("future", "true")], document.Header.Extra);
        Assert.Equal([new TscnField("future_attr", "Vector2(1,   2)"), new TscnField("other", "\"x\"")], document.Nodes[0].Extra);
        string expected =
            "[gd_scene format=3 future=true]\n\n" + "[node name=\"R\" type=\"Node\" future_attr=Vector2(1,   2) other=\"x\"]\n" + "a = 1\n";
        Assert.Equal(expected, TscnWriter.Write(document));
    }

    // resource_format_text.cpp:904, 839, 703: unknown tags and misplaced sections are the loader's own errors.
    [Theory]
    [InlineData("[gd_resource type=\"Resource\" format=3]\n\n[future_tag]\n", "Unknown tag 'future_tag' in file")]
    [InlineData(
        "[gd_scene format=3]\n\n[sub_resource type=\"X\" id=\"X_1\"]\n\n[ext_resource type=\"Script\" path=\"res://a.gd\" id=\"1_a\"]\n",
        "Unknown tag 'ext_resource' in file"
    )]
    [InlineData(
        "[gd_resource type=\"Resource\" format=3]\n\n[resource]\na = 1\n\n[resource]\nb = 2\n",
        "Extra tag found when parsing main resource file"
    )]
    [InlineData(
        "[gd_resource type=\"Resource\" format=3]\n\n[resource]\na = 1\n\n[ext_resource type=\"Script\" path=\"res://a.gd\" id=\"1_a\"]\n",
        "Extra tag found when parsing main resource file"
    )]
    [InlineData(
        "[gd_resource type=\"Resource\" format=3]\n\n[sub_resource type=\"X\" id=\"X_1\"]\na = 1\n",
        "Premature end of file while parsing [sub_resource]"
    )]
    public void RejectsSectionsGodotRejects(string text, string reason) =>
        Assert.Equal(reason, Assert.Throws<TscnParseException>(() => TscnReader.Read(text)).Reason);

    // resource_format_text.cpp:202-204: a node with no name field leaves the name unassigned.
    [Fact]
    public void ReadsANodeWithoutAName() =>
        Assert.Equal("", Assert.Single(TscnReader.Read("[gd_scene format=3]\n\n[node type=\"Node\"]\n").Nodes).Name);

    // variant_parser.cpp:1906-1908: the text ending inside a key is ERR_FILE_EOF, which ends the section normally.
    [Fact]
    public void ReadsASceneWhoseLastLineIsAStrayKey() =>
        Assert.Equal("R", Assert.Single(TscnReader.Read("[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\nfoo").Nodes).Name);

    // resource_format_text.cpp:1127-1136: the loader skips a leading UTF-8 BOM.
    [Fact]
    public void SkipsALeadingByteOrderMark()
    {
        string text = File.ReadAllText(RepoFiles.PathOf("tests", "fixtures", "scenes", "lone_root.tscn"));
        Assert.Equal(text, TscnWriter.Write(TscnReader.Read(new string((char)0xFEFF, 1) + text)));
    }

    // resource_format_text.cpp:206-217, 270-272: parent is read as a NodePath and index as any value _to_int converts.
    [Fact]
    public void ReadsANodePathParentAndAnIntIndex()
    {
        TscnDocument document = TscnReader.Read(
            "[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\n\n[node name=\"C\" parent=NodePath(\"B\") index=3]\n"
        );
        Assert.Equal(("B", (int?)3), (document.Nodes[1].Parent, document.Nodes[1].Index));
    }

    [Fact]
    public void ReadsARepeatedAttributeAsItsLastValue() =>
        Assert.Equal("B", TscnReader.Read("[gd_scene format=3]\n\n[node name=\"A\" name=\"B\"]\n").Nodes[0].Name);

    // resource_format_text.cpp:138-141 and 114: a reference to an id no earlier section defines.
    [Theory]
    [InlineData(
        "[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\nscript = ExtResource(\"1_abcde\")\n",
        "Can't load cached ext-resource id: 1_abcde",
        4
    )]
    [InlineData(
        "[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\nm = [SubResource(\"Late\")]\n\n[sub_resource type=\"X\" id=\"Late\"]\n",
        "Can't load cached sub-resource id: Late",
        4
    )]
    [InlineData("[gd_scene format=3]\n\n[node name=\"R\" instance=ExtResource(\"9\")]\n", "Can't load cached ext-resource id: 9", 3)]
    public void RejectsAReferenceToAnIdNoEarlierSectionDefines(string text, string reason, int line)
    {
        TscnParseException error = Assert.Throws<TscnParseException>(() => TscnReader.Read(text));
        Assert.Equal((reason, line), (error.Reason, error.Line));
    }

    [Theory]
    [InlineData("", "Unexpected end of file")]
    [InlineData("[gd_thing format=3]\n", "Unrecognized file type 'gd_thing'")]
    [InlineData("[gd_scene format=5]\n", "Saved with newer format version")]
    [InlineData("[gd_resource format=3]\n", "Missing 'type' field in 'gd_resource' tag")]
    [InlineData("[gd_scene format=3]\n\n[ext_resource type=\"Script\" id=\"1\"]\n", "Missing 'path' in external resource tag")]
    [InlineData("[gd_scene format=3]\n\n[connection signal=\"s\" to=\".\" method=\"m\"]\n", "missing 'from' field from connection tag")]
    [InlineData("[gd_resource type=\"Resource\" format=3]\n\n[node name=\"R\"]\n", "Unexpected 'node' tag in a resource file")]
    [InlineData("[gd_scene format=3]\n\n[resource]\n", "Unexpected 'resource' tag in a scene file")]
    [InlineData("[gd_scene format=3]\n\n[node name=\"R\"\n", "Unexpected EOF while parsing tag 'node'")]
    [InlineData("[gd_scene format=3]\n\n[node name=\"R\"]\na = Vector2(1)\n", "Expected 2 arguments for constructor")]
    public void RejectsWhatGodotsLoaderRejects(string text, string reason) =>
        Assert.Equal(reason, Assert.Throws<TscnParseException>(() => TscnReader.Read(text)).Reason);

    private static TscnDocument Fixture(string file) => TscnReader.Read(File.ReadAllText(RepoFiles.PathOf("tests", "fixtures", "scenes", file)));
}
