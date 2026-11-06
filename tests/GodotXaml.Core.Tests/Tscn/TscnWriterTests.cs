using System.Collections.Immutable;
using System.Text.RegularExpressions;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Tscn;

public sealed class TscnWriterTests
{
    [Fact]
    public void WritesAResourceBuiltInCodeAsGodotSavedIt()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Resource,
            Header = new TscnHeader { Type = "GradientTexture2D" },
            SubResources =
            [
                new TscnSubResource
                {
                    Type = "Gradient",
                    Id = "Gradient_t3x7r",
                    Properties = [new("colors", new GodotPackedReals(GodotPackedKind.Color, [1, 0, 0, 1, 0, 0, 1, 1]))],
                },
            ],
            ResourceProperties = [new("gradient", new GodotResourceRef(GodotRefKind.Sub, "Gradient_t3x7r")), new("width", new GodotInt(16))],
        };

        Assert.Equal(File.ReadAllText(RepoFiles.PathOf("tests", "fixtures", "scenes", "gradient_texture.tres")), TscnWriter.Write(document));
    }

    // resource_format_text.cpp:2024-2151: attribute order, quoting, sorted groups, "." owner omitted, blank lines.
    [Fact]
    public void WritesASceneBuiltInCodeInTheSaversLayout()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            Header = new TscnHeader { Uid = "uid://abc", Format = 4 },
            ExtResources =
            [
                new TscnExtResource
                {
                    Type = "PackedScene",
                    Uid = "uid://def",
                    Path = "res://a.tscn",
                    Id = "1_aaaaa",
                },
            ],
            Nodes =
            [
                new TscnNode
                {
                    Name = "Root",
                    Type = "Node",
                    UniqueId = 5,
                },
                new TscnNode
                {
                    Name = "Child \"1\"",
                    Parent = ".",
                    ParentIdPath = [5],
                    Owner = "Inst",
                    OwnerUidPath = [5, 6],
                    Index = 2,
                    UniqueId = 7,
                    NodePaths = ["target"],
                    Groups = ["zeta", "alpha"],
                    Instance = new GodotResourceRef(GodotRefKind.External, "1_aaaaa"),
                    Properties = [new("with space", new GodotFloat(1))],
                },
                new TscnNode
                {
                    Name = "Plain",
                    Type = "Node",
                    Parent = ".",
                    Owner = ".",
                },
            ],
            Connections =
            [
                new TscnConnection
                {
                    Signal = "s",
                    From = ".",
                    To = "Plain",
                    Method = "m",
                },
                new TscnConnection
                {
                    Signal = "t",
                    From = ".",
                    To = "Plain",
                    Method = "n",
                    Flags = 3,
                    Unbinds = 1,
                    Binds = [new GodotInt(4)],
                    FromUidPath = [5],
                },
            ],
            Editables = [new TscnEditable { Path = "Child" }],
        };

        string expected =
            "[gd_scene format=3 uid=\"uid://abc\"]\n\n"
            + "[ext_resource type=\"PackedScene\" uid=\"uid://def\" path=\"res://a.tscn\" id=\"1_aaaaa\"]\n\n"
            + "[node name=\"Root\" type=\"Node\" unique_id=5]\n\n"
            + "[node name=\"Child \\\"1\\\"\" parent=\".\" parent_id_path=PackedInt32Array(5) owner=\"Inst\" owner_uid_path=PackedInt32Array(5, 6)"
            + " index=\"2\" unique_id=7 node_paths=PackedStringArray(\"target\") groups=[\"alpha\", \"zeta\"] instance=ExtResource(\"1_aaaaa\")]\n"
            + "\"with space\" = 1.0\n\n"
            + "[node name=\"Plain\" type=\"Node\" parent=\".\"]\n\n"
            + "[connection signal=\"s\" from=\".\" to=\"Plain\" method=\"m\"]\n"
            + "[connection signal=\"t\" from=\".\" to=\"Plain\" method=\"n\" flags=3 from_uid_path=PackedInt32Array(5) unbinds=1 binds= [4]]\n\n"
            + "[editable path=\"Child\"]\n";
        Assert.Equal(expected, TscnWriter.Write(document));
    }

    // resource_format_text.cpp:1736-1744, 1810: format 4 for a PackedByteArray over 64 bytes or any PackedVector4Array,
    // and values written with compat off; the format read from the file is not trusted.
    [Theory]
    [InlineData(64, false, 3)]
    [InlineData(65, false, 4)]
    [InlineData(0, true, 4)]
    public void RecomputesTheFormatFromTheValues(int byteCount, bool vector4, int format)
    {
        GodotValue nested = new GodotArray([new GodotPackedBytes([.. new byte[byteCount]])], null);
        ImmutableArray<TscnProperty> properties = vector4 ? [new("v", new GodotPackedReals(GodotPackedKind.Vector4, []))] : [new("b", nested)];
        var document = new TscnDocument
        {
            Kind = TscnKind.Resource,
            Header = new TscnHeader { Type = "Resource", Format = 7 - format },
            ResourceProperties = properties,
        };

        string text = TscnWriter.Write(document);

        Assert.StartsWith($"[gd_resource type=\"Resource\" format={format}]\n\n[resource]\n", text, StringComparison.Ordinal);
        Assert.Equal(format, TscnWriter.FormatOf(document));
        string bytes =
            byteCount == 0 ? ""
            : format == 4 ? "PackedByteArray(\""
            : "PackedByteArray(0, 0";
        Assert.Contains(bytes, text, StringComparison.Ordinal);
    }

    [Fact]
    public void MintsStableIdsForASeed()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            ExtResources =
            [
                new TscnExtResource
                {
                    Type = "Script",
                    Path = "res://a.gd",
                    Id = "1_aaaaa",
                },
            ],
        };

        string ext = document.NewExtResourceId(42);
        string sub = TscnDocument.NewSubResourceId("Gradient", 42, SubIds(document));

        Assert.Equal(ext, document.NewExtResourceId(42));
        Assert.Equal(sub, TscnDocument.NewSubResourceId("Gradient", 42, SubIds(document)));
        Assert.Matches(new Regex("^2_[a-z0-9]{5}$"), ext);
        Assert.Matches(new Regex("^Gradient_[a-z0-9]{5}$"), sub);
        Assert.NotEqual(ext, document.NewExtResourceId(43));
    }

    [Fact]
    public void MintsAnIdNotAlreadyTaken()
    {
        var empty = new TscnDocument { Kind = TscnKind.Scene };
        string taken = TscnDocument.NewSubResourceId("X", 7, SubIds(empty));
        TscnDocument document = empty with { SubResources = [new TscnSubResource { Type = "X", Id = taken }] };

        string minted = TscnDocument.NewSubResourceId("X", 7, SubIds(document));

        Assert.NotEqual(taken, minted);
        Assert.Equal(minted, TscnDocument.NewSubResourceId("X", 7, SubIds(document)));
    }

    // A document's kind holds only its own sections; the writer names the field it cannot write.
    [Fact]
    public void RefusesAResourceDocumentWithNodes()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Resource,
            Header = new TscnHeader { Type = "Resource" },
            Nodes = [new TscnNode { Name = "R" }],
        };
        Assert.Contains("Nodes", Assert.Throws<InvalidOperationException>(() => TscnWriter.Write(document)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAResourceDocumentWithConnections()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Resource,
            Header = new TscnHeader { Type = "Resource" },
            Connections =
            [
                new TscnConnection
                {
                    Signal = "s",
                    From = ".",
                    To = ".",
                    Method = "m",
                },
            ],
        };
        Assert.Contains("Connections", Assert.Throws<InvalidOperationException>(() => TscnWriter.Write(document)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAResourceDocumentWithEditables()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Resource,
            Header = new TscnHeader { Type = "Resource" },
            Editables = [new TscnEditable { Path = "A" }],
        };
        Assert.Contains("Editables", Assert.Throws<InvalidOperationException>(() => TscnWriter.Write(document)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesASceneDocumentWithResourceProperties()
    {
        var document = new TscnDocument { Kind = TscnKind.Scene, ResourceProperties = [new("a", new GodotInt(1))] };
        Assert.Contains(
            "ResourceProperties",
            Assert.Throws<InvalidOperationException>(() => TscnWriter.Write(document)).Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RefusesAResourceDocumentWithSceneProperties()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Resource,
            Header = new TscnHeader { Type = "Resource" },
            SceneProperties = [new("a", new GodotInt(1))],
        };
        Assert.Contains(
            "SceneProperties",
            Assert.Throws<InvalidOperationException>(() => TscnWriter.Write(document)).Message,
            StringComparison.Ordinal
        );
    }

    // Godot 4.8 resource_format_text.cpp:2122-2125: only a PackedScene sub-resource writes nodes of its own.
    [Fact]
    public void RefusesANestedSceneOnASubResourceThatIsNotAPackedScene()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            SubResources =
            [
                new TscnSubResource
                {
                    Type = "Resource",
                    Id = "Resource_1",
                    Scene = new TscnNestedScene { Nodes = [new TscnNode { Name = "R", Type = "Node" }] },
                },
            ],
            Nodes = [new TscnNode { Name = "R", Type = "Node" }],
        };
        Assert.Contains("Scene", Assert.Throws<InvalidOperationException>(() => TscnWriter.Write(document)).Message, StringComparison.Ordinal);
    }

    // A nested scene in place of _bundled cannot hold _bundled too, and connections or editables need a node to be on.
    [Fact]
    public void RefusesANestedSceneOnASubResourceWithBundled()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            SubResources =
            [
                new TscnSubResource
                {
                    Type = "PackedScene",
                    Id = "P",
                    Properties = [new("_bundled", new GodotInt(0))],
                    Scene = new TscnNestedScene { Nodes = [new TscnNode { Name = "R", Type = "Node" }] },
                },
            ],
            Nodes = [new TscnNode { Name = "R", Type = "Node" }],
        };
        Assert.Contains("_bundled", Assert.Throws<InvalidOperationException>(() => TscnWriter.Write(document)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RefusesANestedSceneWithNoNodeButConnectionsOrEditables(bool connection)
    {
        TscnNestedScene scene = connection
            ? new TscnNestedScene
            {
                Connections =
                [
                    new TscnConnection
                    {
                        Signal = "s",
                        From = ".",
                        To = ".",
                        Method = "m",
                    },
                ],
            }
            : new TscnNestedScene { Editables = [new TscnEditable { Path = "A" }] };
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            SubResources =
            [
                new TscnSubResource
                {
                    Type = "PackedScene",
                    Id = "P",
                    Scene = scene,
                },
            ],
            Nodes = [new TscnNode { Name = "R", Type = "Node" }],
        };
        Assert.Contains("no node", Assert.Throws<InvalidOperationException>(() => TscnWriter.Write(document)).Message, StringComparison.Ordinal);
    }

    // Godot 4.8's own output, saved by Godot 4.8 beta 2 (resource_format_text.cpp:2025-2143, 1721-1866), reads and
    // writes back to the same bytes.
    [Fact]
    public void MetaWritesBackByteIdentical() => AssertWritesBack("meta.tscn");

    [Fact]
    public void NestedWritesBackByteIdentical() => AssertWritesBack("nested.tscn");

    [Fact]
    public void NestedOnlyWritesBackByteIdentical() => AssertWritesBack("nested_only.tscn");

    [Fact]
    public void HolderWritesBackByteIdentical() => AssertWritesBack("holder.tres");

    // 4.8 :2122-2130: an unpacked PackedScene sub-resource has no nodes, and the scene still gets its bare [resource].
    [Fact]
    public void EmptyNestedWritesBackByteIdentical() => AssertWritesBack("empty_nested.tscn");

    // resource_format_text.cpp:2060: groups.sort_custom<StringName::AlphCompare>, which is str_compare, code point order.
    [Fact]
    public void SortsGroupsByCodePoint()
    {
        var document = new TscnDocument
        {
            Kind = TscnKind.Scene,
            Nodes = [new TscnNode { Name = "R", Groups = ["\U0001F600", new string((char)0xFF21, 1)] }],
        };

        Assert.Contains("groups=[\"" + (char)0xFF21 + "\", \"\U0001F600\"]", TscnWriter.Write(document), StringComparison.Ordinal);
    }

    // core/io/resource.cpp:151-163: base ('z' - 'a') + ('9' - '0') = 34 characters, 'a'..'y' and '0'..'8'.
    [Fact]
    public void MintsIdsFromGodotsAlphabet()
    {
        var document = new TscnDocument { Kind = TscnKind.Scene };
        for (ulong seed = 0; seed < 200; seed++)
        {
            string id = TscnDocument.NewSubResourceId("Gradient", seed, SubIds(document));
            Assert.DoesNotContain('z', id);
            Assert.DoesNotContain('9', id);
        }
    }

    // ustring.cpp:5053-5064.
    [Theory]
    [InlineData("plain/name_1", "plain/name_1")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("a=b", "\"a=b\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("a[0]", "\"a[0]\"")]
    [InlineData("naïve", "\"naïve\"")]
    [InlineData("tab\there", "\"tab\there\"")]
    public void QuotesThePropertyNamesGodotQuotes(string name, string encoded) => Assert.Equal(encoded, TscnWriter.EncodePropertyName(name));

    private static void AssertWritesBack(string file)
    {
        string text = File.ReadAllText(RepoFiles.PathOf("tests", "fixtures", "tscn48", file));
        Assert.Equal(text, TscnWriter.Write(TscnReader.Read(text)));
    }

    private static HashSet<string> SubIds(TscnDocument document) => new(document.SubResources.Select(sub => sub.Id), StringComparer.Ordinal);
}
