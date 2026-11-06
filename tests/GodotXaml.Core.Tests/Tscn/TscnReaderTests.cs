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

    // variant_parser.cpp:1933 keeps only the characters above ' ' in an unquoted key, so Godot drops a raw control byte
    // and the scanner does the same.
    [Fact]
    public void DropsARawControlByteFromAnUnquotedKey()
    {
        TscnDocument document = TscnReader.Read("[gd_resource type=\"Resource\" format=3]\n\n[resource]\nmetadata/a\u0001b = 1\n");

        Assert.Equal("metadata/ab", Assert.Single(document.ResourceProperties).Name);
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

    // resource_format_text.cpp:904, 839: unknown tags and misplaced sections are the loader's own errors.
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
    // :532-537, 646-651: a resource file that ends inside its resource loops returns the parser's ERR_FILE_EOF,
    // which load() reports with no message of its own; GXAML-40 measured it and names it "Unexpected end of file".
    [InlineData("[gd_resource type=\"Resource\" format=3]\n\n[sub_resource type=\"X\" id=\"X_1\"]\na = 1\n", "Unexpected end of file")]
    [InlineData(
        "[gd_resource type=\"Resource\" format=3]\n\n[ext_resource type=\"Script\" path=\"res://a.gd\" id=\"1_a\"]\n",
        "Unexpected end of file"
    )]
    // :1193-1200: the tag read after a header that has nothing after it is ERR_FILE_CORRUPT "Unexpected end of file".
    [InlineData("[gd_resource type=\"Resource\" format=3]\n", "Unexpected end of file")]
    [InlineData("[gd_resource type=\"Resource\" format=3]\n\n", "Unexpected end of file")]
    [InlineData("[gd_resource type=\"Resource\" format=3]", "Unexpected end of file")]
    public void RejectsSectionsGodotRejects(string text, string reason) =>
        Assert.Equal(reason, Assert.Throws<TscnParseException>(() => TscnReader.Read(text)).Reason);

    // resource_format_text.cpp:461-538, 547-717, 868-908: the tag after the resource tags must be [node]. The last
    // resource's trailing parse_tag ends the file with ERR_FILE_EOF (:532-537), which load() reports with no message;
    // a file with no tag at all ends at open():1194-1200, "Unexpected end of file".
    [Theory]
    [InlineData("")]
    [InlineData("\n\n[ext_resource type=\"Script\" path=\"res://a.gd\" id=\"1\"]\n")]
    public void RejectsASceneThatEndsBeforeItsFirstNode(string tail) =>
        Assert.Equal("Unexpected end of file", Assert.Throws<TscnParseException>(() => TscnReader.Read("[gd_scene format=3]" + tail)).Reason);

    // resource_format_text.cpp:904: a tag after the resources that is not a node is named as unknown.
    [Theory]
    [InlineData("[connection signal=\"s\" from=\".\" to=\".\" method=\"m\"]", "Unknown tag 'connection' in file")]
    [InlineData("[editable path=\".\"]", "Unknown tag 'editable' in file")]
    public void RejectsASceneWhoseOnlyTagIsNotANode(string tag, string reason) =>
        Assert.Equal(reason, Assert.Throws<TscnParseException>(() => TscnReader.Read($"[gd_scene format=3]\n\n{tag}\n")).Reason);

    // resource_format_text.cpp:868-908: the first tag after a scene's resources must be [node]; _parse_node_tag
    // (:190-419) handles connection and editable only once it is inside it.
    [Theory]
    [InlineData("[connection signal=\"s\" from=\".\" to=\".\" method=\"m\"]", "Unknown tag 'connection' in file")]
    [InlineData("[editable path=\".\"]", "Unknown tag 'editable' in file")]
    public void RejectsAConnectionOrEditableBeforeTheFirstNode(string tag, string reason) =>
        Assert.Equal(
            reason,
            Assert.Throws<TscnParseException>(() => TscnReader.Read($"[gd_scene format=3]\n\n{tag}\n\n[node name=\"R\" type=\"Node\"]\n")).Reason
        );

    // resource_format_text.cpp:487-489, 566-567, 1153-1188 and core/variant/variant.cpp:1558-1560, 1597-1642: a
    // tag's string fields are read through Variant::operator String, so a StringName, a bool, an int, a NodePath and
    // null convert to the text stringify gives them.
    [Theory]
    [InlineData("type", "&\"Texture2D\"", "Texture2D")]
    [InlineData("type", "false", "false")]
    [InlineData("id", "1", "1")]
    [InlineData("path", "NodePath(\"res://a.png\")", "res://a.png")]
    [InlineData("uid", "null", "<null>")]
    public void ReadsAStringFieldAsGodotsVariantToStringGivesIt(string name, string value, string expected)
    {
        var fields = new Dictionary<string, string>
        {
            ["type"] = "\"Script\"",
            ["path"] = "\"res://a.gd\"",
            ["id"] = "\"1\"",
        };
        fields[name] = value;
        string tag = "[ext_resource " + string.Join(" ", fields.Select(field => field.Key + "=" + field.Value)) + "]";
        TscnExtResource ext = Assert.Single(TscnReader.Read($"[gd_scene format=3]\n\n{tag}\n\n[node name=\"R\"]\n").ExtResources);

        Assert.Equal(
            expected,
            name switch
            {
                "type" => ext.Type,
                "path" => ext.Path,
                "id" => ext.Id,
                _ => ext.Uid,
            }
        );
    }

    // A float, a math value, a container and a reference keep the reader's own error: stringify would print one, and
    // Godot's saver never writes one in a string field.
    [Fact]
    public void RejectsAFloatInAStringField() =>
        Assert.Equal(
            "Expected string for 'type' in 'ext_resource' tag",
            Assert
                .Throws<TscnParseException>(() =>
                    TscnReader.Read("[gd_scene format=3]\n\n[ext_resource type=1.5 path=\"res://a.gd\" id=\"1\"]\n\n[node name=\"R\"]\n")
                )
                .Reason
        );

    // resource_format_text.cpp:202-204: a node with no name field leaves the name unassigned.
    // resource_format_text.cpp:244 reads instance_placeholder through Variant::operator String (stringify), not
    // through operator StringName or operator NodePath, so a number keeps its digits.
    [Fact]
    public void ReadsAnInstancePlaceholderAsItsStringValue() =>
        Assert.Equal(
            "5",
            TscnReader
                .Read("[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\n\n[node name=\"P\" parent=\".\" instance_placeholder=5]\n")
                .Nodes[1]
                .InstancePlaceholder
        );

    // resource_format_text.cpp:243-253: a placeholder on the first node, which has nothing to inherit from, is
    // ERR_FILE_CORRUPT with this text.
    [Fact]
    public void RejectsAPlaceholderOnTheFirstNode() =>
        Assert.Equal(
            "Instance Placeholder can't be used for inheritance",
            Assert
                .Throws<TscnParseException>(() => TscnReader.Read("[gd_scene format=3]\n\n[node name=\"P\" instance_placeholder=\"res://a.tscn\"]\n"))
                .Reason
        );

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

    // core/variant/variant.cpp:1539-1547, 1992-2000: operator StringName and operator NodePath give an empty name or
    // path for any value that is neither a string nor one of their own types, so a number converts rather than fails.
    [Fact]
    public void ReadsANumberInANameOrPathFieldAsEmpty()
    {
        TscnDocument document = TscnReader.Read("[gd_scene format=3]\n\n[node name=1 type=\"Node\"]\n\n[node name=\"C\" type=\"Node\" parent=1]\n");
        Assert.Equal(("", ""), (document.Nodes[0].Name, document.Nodes[1].Parent));
    }

    // resource_format_text.cpp:203, 220 and core/variant/variant.cpp:1539-1547: name and type are read as
    // StringName, so a NodePath in either gives the empty name.
    [Theory]
    [InlineData("[node name=NodePath(\"A\") type=\"Node\"]", "", "Node")]
    [InlineData("[node name=\"R\" type=NodePath(\"Node\")]", "R", "")]
    public void ReadsANodePathInANameFieldAsEmpty(string tag, string name, string type)
    {
        TscnNode node = Assert.Single(TscnReader.Read($"[gd_scene format=3]\n\n{tag}\n").Nodes);
        Assert.Equal((name, type), (node.Name, node.Type));
    }

    // The control: a StringName in a name field keeps its own text, as :203's StringName conversion does.
    [Fact]
    public void ReadsAStringNameInANameFieldAsItsText() =>
        Assert.Equal("A", Assert.Single(TscnReader.Read("[gd_scene format=3]\n\n[node name=&\"A\"]\n").Nodes).Name);

    // resource_format_text.cpp:207 and core/variant/variant.cpp:1992-2000: parent is read as a NodePath, so a
    // StringName in it gives the empty path.
    [Fact]
    public void ReadsAStringNameInAParentFieldAsEmpty() =>
        Assert.Equal(
            "",
            TscnReader
                .Read("[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\n\n[node name=\"C\" parent=&\"B\" type=\"Node\"]\n")
                .Nodes[1]
                .Parent
        );

    // resource_format_text.cpp:263 and core/variant/variant.cpp:1992-2000: owner is read as a NodePath, so a
    // StringName in it gives the empty path.
    [Fact]
    public void ReadsAStringNameInAnOwnerFieldAsEmpty() =>
        Assert.Equal(
            "",
            TscnReader.Read("[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\n\n[node name=\"C\" parent=\".\" owner=&\"B\"]\n").Nodes[1].Owner
        );

    // resource_format_text.cpp:336-339 and core/variant/variant.cpp:1539-1547, 1992-2000: from and to are NodePaths
    // and signal and method StringNames, so the type the other field takes gives the empty text.
    [Fact]
    public void ReadsTheOtherFieldsTypeInConnectionFieldsAsEmpty()
    {
        TscnConnection connection = Assert.Single(
            TscnReader
                .Read(
                    "[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\n\n"
                        + "[connection signal=NodePath(\"s\") from=&\"A\" to=&\"B\" method=NodePath(\"m\")]\n"
                )
                .Connections
        );
        Assert.Equal(("", "", "", ""), (connection.Signal, connection.From, connection.To, connection.Method));
    }

    // resource_format_text.cpp:399 and core/variant/variant.cpp:1992-2000: the editable path is read as a NodePath,
    // so a StringName in it gives the empty path.
    [Fact]
    public void ReadsAStringNameInAnEditablePathAsEmpty() =>
        Assert.Equal(
            "",
            Assert.Single(TscnReader.Read("[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\n\n[editable path=&\"X\"]\n").Editables).Path
        );

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
    [InlineData(
        "[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\n\n[connection signal=\"s\" to=\".\" method=\"m\"]\n",
        "missing 'from' field from connection tag"
    )]
    [InlineData("[gd_resource type=\"Resource\" format=3]\n\n[node name=\"R\"]\n", "Unexpected 'node' tag in a resource file")]
    [InlineData("[gd_scene format=3]\n\n[resource]\n", "Scene files need to have at least one node. None was found.")]
    [InlineData("[gd_scene format=3]\n\n[node name=\"R\"\n", "Unexpected EOF while parsing tag 'node'")]
    [InlineData("[gd_scene format=3]\n\n[node name=\"R\"]\na = Vector2(1)\n", "Expected 2 arguments for constructor")]
    public void RejectsWhatGodotsLoaderRejects(string text, string reason) =>
        Assert.Equal(reason, Assert.Throws<TscnParseException>(() => TscnReader.Read(text)).Reason);

    [Fact]
    public void ReadUidReturnsTheHeadersUid() =>
        Assert.Equal("uid://abc", TscnReader.ReadUid("[gd_scene format=3 uid=\"uid://abc\"]\n\n[node name=\"Root\" type=\"Node\"]\n"));

    [Fact]
    public void ReadUidIsNullWithoutAUid() => Assert.Null(TscnReader.ReadUid("[gd_scene format=3]\n\n[node name=\"Root\" type=\"Node\"]\n"));

    // resource_format_text.cpp:1127-1136: the loader skips a leading UTF-8 BOM.
    [Fact]
    public void ReadUidSkipsALeadingByteOrderMark() =>
        Assert.Equal("uid://abc", TscnReader.ReadUid((char)0xFEFF + "[gd_scene format=3 uid=\"uid://abc\"]\n"));

    [Fact]
    public void ReadUidThrowsOnABadHeader() => Assert.Throws<TscnParseException>(() => TscnReader.ReadUid("[node name=\"Root\" type=\"Node\"]\n"));

    [Fact]
    public void ReadUidIgnoresABrokenBody()
    {
        const string Text = "[gd_scene format=3 uid=\"uid://abc\"]\n\n[node name=\"Root\"\n";

        Assert.Equal("uid://abc", TscnReader.ReadUid(Text));
        Assert.Throws<TscnParseException>(() => TscnReader.Read(Text));
    }

    // Godot 4.8 (resource_format_text.cpp:2114-2117, 2137-2139): the scene's own properties in a [resource] section
    // before the first node.
    [Fact]
    public void ReadsMeta()
    {
        TscnDocument document = Fixture48("meta.tscn");

        Assert.Equal(TscnKind.Scene, document.Kind);
        Assert.Empty(document.SubResources);
        Assert.True(document.HasSceneResourceSection);
        Assert.Equal(
            [new TscnProperty("resource_name", new GodotString("Hello")), new TscnProperty("metadata/answer", new GodotInt(42))],
            document.SceneProperties
        );
        Assert.Equal(["Root", "Child"], document.Nodes.Select(node => node.Name));
    }

    // Godot 4.8 (resource_format_text.cpp:674-681, 402-404, 2122-2125): a PackedScene sub-resource's nodes and
    // connection follow its header, ended by the next sub-resource; the scene's [resource] refers to both.
    [Fact]
    public void ReadsNested()
    {
        TscnDocument document = Fixture48("nested.tscn");

        Assert.Equal(["PackedScene", "Resource"], document.SubResources.Select(sub => sub.Type));
        TscnSubResource packed = document.SubResources[0];
        Assert.Empty(packed.Properties);
        AssertInnerScene(packed.Scene);
        Assert.Null(document.SubResources[1].Scene);
        Assert.Equal([new TscnProperty("resource_name", new GodotString("Other"))], document.SubResources[1].Properties);
        Assert.Equal(
            [
                new TscnProperty("metadata/inner", new GodotResourceRef(GodotRefKind.Sub, packed.Id)),
                new TscnProperty("metadata/other", new GodotResourceRef(GodotRefKind.Sub, document.SubResources[1].Id)),
            ],
            document.SceneProperties
        );
        Assert.Equal(["Root", "Child"], document.Nodes.Select(node => node.Name));
        Assert.Empty(document.Connections);
    }

    // Godot 4.8 (resource_format_text.cpp:2127-2130): a scene with a nested scene and no property of its own gets an
    // empty [resource] section.
    [Fact]
    public void ReadsNestedOnly()
    {
        TscnDocument document = Fixture48("nested_only.tscn");

        TscnSubResource packed = Assert.Single(document.SubResources);
        AssertInnerScene(packed.Scene);
        Assert.Empty(document.SceneProperties);
        Assert.Equal(["Root", "Child"], document.Nodes.Select(node => node.Name));
        Assert.Equal(
            new TscnProperty("metadata/inner", new GodotResourceRef(GodotRefKind.Sub, packed.Id)),
            Assert.Single(document.Nodes[0].Properties)
        );
    }

    // Godot 4.8 (resource_format_text.cpp:674-681, 402-404): a .tres's PackedScene sub-resource holds its nodes too.
    [Fact]
    public void ReadsHolder()
    {
        TscnDocument document = Fixture48("holder.tres");

        Assert.Equal(TscnKind.Resource, document.Kind);
        TscnSubResource packed = Assert.Single(document.SubResources);
        Assert.NotNull(packed.Scene);
        Assert.Equal(["HeldRoot", "HeldChild"], packed.Scene.Nodes.Select(node => node.Name));
        Assert.Empty(packed.Scene.Connections);
        Assert.Empty(document.Nodes);
        Assert.Equal(
            new TscnProperty("metadata/scene", new GodotResourceRef(GodotRefKind.Sub, packed.Id)),
            Assert.Single(document.ResourceProperties)
        );
    }

    // Godot 4.8 resource_format_text.cpp:758-764: a scene that ends inside its [resource] has no node.
    [Fact]
    public void RejectsASceneThatEndsInsideItsResource() =>
        Assert.Equal(
            "Scene files need to have at least one node. None was found.",
            Assert.Throws<TscnParseException>(() => TscnReader.Read("[gd_scene format=3]\n\n[resource]\nmetadata/a = 1\n")).Reason
        );

    // Godot 4.8 resource_format_text.cpp:402-408: the main scene is not nested, so a sub-resource or a [resource]
    // after its first node is unknown.
    [Theory]
    [InlineData("[sub_resource type=\"Resource\" id=\"R_1\"]", "Unknown tag 'sub_resource' in file")]
    [InlineData("[resource]", "Unknown tag 'resource' in file")]
    public void RejectsAResourceSectionAfterTheMainScenesFirstNode(string tag, string reason) =>
        Assert.Equal(
            reason,
            Assert.Throws<TscnParseException>(() => TscnReader.Read($"[gd_scene format=3]\n\n[node name=\"R\" type=\"Node\"]\n\n{tag}\n")).Reason
        );

    // Godot 4.8's loader (resource_format_text.cpp:674-681) would read the node into the sub-resource; the reader
    // keeps it in the main scene, since a PackedScene with _bundled was saved by 4.7 and holds no nested sections.
    [Fact]
    public void AFourSevenSceneWhoseLastSubResourceIsABundledPackedSceneKeepsItsMainNodes()
    {
        TscnDocument document = TscnReader.Read(
            "[gd_scene format=3]\n\n[sub_resource type=\"PackedScene\" id=\"P\"]\n_bundled = {\n\"names\": PackedStringArray()\n}\n\n"
                + "[node name=\"Root\" type=\"Node\"]\n"
        );

        Assert.Equal("Root", Assert.Single(document.Nodes).Name);
        Assert.Null(Assert.Single(document.SubResources).Scene);
    }

    // Godot 4.7.2 (resource_format_text.cpp:1986-1988) skips a property equal to its default, so an unpacked PackedScene
    // has no _bundled; a 4.8 save always ends a nested scene with [sub_resource] or [resource] (4.8 :2122-2134), so a
    // nested scene still open at the end of a scene with no main node is the main scene.
    [Fact]
    public void AFourSevenSceneWhoseLastSubResourceIsAnEmptyPackedSceneKeepsItsMainNodes()
    {
        TscnDocument document = TscnReader.Read(
            "[gd_scene format=3]\n\n[sub_resource type=\"PackedScene\" id=\"P\"]\n\n"
                + "[node name=\"Root\" type=\"Node\"]\nmetadata/s = SubResource(\"P\")\n"
        );

        TscnNode root = Assert.Single(document.Nodes);
        Assert.Equal("Root", root.Name);
        Assert.Equal(new TscnProperty("metadata/s", new GodotResourceRef(GodotRefKind.Sub, "P")), Assert.Single(root.Properties));
        Assert.Null(Assert.Single(document.SubResources).Scene);
        Assert.False(document.HasSceneResourceSection);
    }

    private static void AssertInnerScene(TscnNestedScene? scene)
    {
        Assert.NotNull(scene);
        Assert.Equal(["InnerRoot", "InnerChild"], scene.Nodes.Select(node => node.Name));
        TscnConnection connection = Assert.Single(scene.Connections);
        Assert.Equal(("visibility_changed", "InnerChild", ".", "show"), (connection.Signal, connection.From, connection.To, connection.Method));
        Assert.Empty(scene.Editables);
    }

    private static TscnDocument Fixture(string file) => TscnReader.Read(File.ReadAllText(RepoFiles.PathOf("tests", "fixtures", "scenes", file)));

    private static TscnDocument Fixture48(string file) => TscnReader.Read(File.ReadAllText(RepoFiles.PathOf("tests", "fixtures", "tscn48", file)));
}
