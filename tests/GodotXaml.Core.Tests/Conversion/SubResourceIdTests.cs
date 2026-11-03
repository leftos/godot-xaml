using System.Text;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// A minted sub_resource id depends on where the resource is used, so an edit elsewhere in the file leaves it unchanged
// and every load of the same file gives it the same id.
public sealed class SubResourceIdTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new() { Seed = 7 };

    public static TheoryData<string> Goldens
    {
        get
        {
            TheoryData<string> goldens = [];
            foreach (string file in Directory.GetFiles(RepoFiles.PathOf("tests", "fixtures", "converted"), "*.xscn").Order(StringComparer.Ordinal))
            {
                goldens.Add(Path.GetFileName(file));
            }

            return goldens;
        }
    }

    [Fact]
    public void IdsDoNotDependOnPropertyOrder()
    {
        const string textureFirst = $$"""
            <Node2D {{X}} Name="Root">
              <Sprite2D Name="One" Visible="False" Modulate="1, 0, 0, 1">
                <Sprite2D.Texture><GradientTexture2D Width="8" Height="4" /></Sprite2D.Texture>
                <Sprite2D.Material><CanvasItemMaterial LightMode="Unshaded" BlendMode="Add" /></Sprite2D.Material>
              </Sprite2D>
            </Node2D>
            """;
        const string materialFirst = $$"""
            <Node2D {{X}} Name="Root">
              <Sprite2D Name="One" Modulate="1, 0, 0, 1" Visible="False">
                <Sprite2D.Material><CanvasItemMaterial BlendMode="Add" LightMode="Unshaded" /></Sprite2D.Material>
                <Sprite2D.Texture><GradientTexture2D Height="4" Width="8" /></Sprite2D.Texture>
              </Sprite2D>
            </Node2D>
            """;

        Assert.Equal(NodeSubIds(Back(textureFirst)), NodeSubIds(Back(materialFirst)));
    }

    // A same-typed resource on an earlier node is found first; the ones found after it keep their ids.
    [Fact]
    public void AnInsertedResourceKeepsTheOthersIds()
    {
        const string before = $$"""
            <Node2D {{X}} Name="Root">
              <Sprite2D Name="One">
                <Sprite2D.Material><CanvasItemMaterial LightMode="Unshaded" /></Sprite2D.Material>
              </Sprite2D>
              <Sprite2D Name="Two">
                <Sprite2D.Material><CanvasItemMaterial BlendMode="Add" /></Sprite2D.Material>
                <Sprite2D.Texture><GradientTexture2D Width="8" /></Sprite2D.Texture>
              </Sprite2D>
            </Node2D>
            """;
        const string after = $$"""
            <Node2D {{X}} Name="Root">
              <Sprite2D Name="Zero">
                <Sprite2D.Material><CanvasItemMaterial BlendMode="Sub" /></Sprite2D.Material>
              </Sprite2D>
              <Sprite2D Name="One">
                <Sprite2D.Material><CanvasItemMaterial LightMode="Unshaded" /></Sprite2D.Material>
              </Sprite2D>
              <Sprite2D Name="Two">
                <Sprite2D.Material><CanvasItemMaterial BlendMode="Add" /></Sprite2D.Material>
                <Sprite2D.Texture><GradientTexture2D Width="8" /></Sprite2D.Texture>
              </Sprite2D>
            </Node2D>
            """;

        Dictionary<string, string> kept = NodeSubIds(Back(before));
        Dictionary<string, string> grown = NodeSubIds(Back(after));

        Assert.Equal(3, kept.Count);
        Assert.All(kept, pair => Assert.Equal(pair.Value, grown[pair.Key]));
    }

    // Every use of an entry names one sub_resource, whose id stays when another node uses the entry first and an inline
    // resource of the same class is found before it.
    [Fact]
    public void ATableEntryHasOneIdForEveryUse()
    {
        const string shared = $$"""
            <Node2D {{X}} Name="Root">
              <x:Resources>
                <CanvasItemMaterial x:Key="Shared" LightMode="Unshaded" />
              </x:Resources>
              <Sprite2D Name="Two" Material="{Shared}" />
              <Sprite2D Name="Three" Material="{Shared}" />
            </Node2D>
            """;
        const string usedElsewhere = $$"""
            <Node2D {{X}} Name="Root">
              <x:Resources>
                <CanvasItemMaterial x:Key="Shared" LightMode="Unshaded" />
              </x:Resources>
              <Sprite2D Name="One">
                <Sprite2D.Material><CanvasItemMaterial BlendMode="Add" /></Sprite2D.Material>
              </Sprite2D>
              <Sprite2D Name="Four" Material="{Shared}" />
            </Node2D>
            """;

        XscnDocument document = Parse(shared);
        ConversionResult<TscnDocument> back = Convert(document);
        string id = Assert.IsType<ConversionIds>(back.Ids).SubIdOf(Assert.Single(document.Resources))!;
        Dictionary<string, string> uses = NodeSubIds(back.Document);
        string elsewhere = NodeSubIds(Back(usedElsewhere))["Root/Four|material"];

        Assert.Equal([id, id], uses.Values);
        Assert.Equal(id, Assert.Single(back.Document.SubResources).Id);
        Assert.Equal(id, elsewhere);
    }

    [Theory]
    [MemberData(nameof(Goldens))]
    public void IdsSurviveWriteAndParse(string golden)
    {
        XscnDocument document = Parse(ReadGolden(golden));
        XscnDocument again = Parse(XscnWriter.Write(document, Metadata));

        TscnDocument first = XscnToTscn.Convert(document, Metadata, Options).Document;
        TscnDocument second = XscnToTscn.Convert(again, Metadata, Options).Document;

        Assert.Equal(first.SubResources.Select(sub => sub.Id), second.SubResources.Select(sub => sub.Id));
        Assert.Equal(TscnWriter.Write(first), TscnWriter.Write(second));
    }

    public static TheoryData<string> SceneFixtures
    {
        get
        {
            TheoryData<string> scenes = [];
            foreach (string file in Directory.GetFiles(RepoFiles.PathOf("tests", "fixtures", "scenes"), "*.tscn").Order(StringComparer.Ordinal))
            {
                scenes.Add(Path.GetFileName(file));
            }

            return scenes;
        }
    }

    // A document the forward conversion made, not one parsed, mints the ids its written and parsed text mints.
    [Theory]
    [MemberData(nameof(SceneFixtures))]
    public void IdsSurviveWriteAndParseFromAConversion(string scene)
    {
        TscnDocument source = TscnReader.Read(File.ReadAllText(RepoFiles.PathOf("tests", "fixtures", "scenes", scene)));
        XscnDocument document = TscnToXscn.Convert(source, Metadata, Options).Document;
        XscnDocument again = Parse(XscnWriter.Write(document, Metadata));

        TscnDocument first = XscnToTscn.Convert(document, Metadata, Options).Document;
        TscnDocument second = XscnToTscn.Convert(again, Metadata, Options).Document;

        Assert.Equal(first.SubResources.Select(sub => sub.Id), second.SubResources.Select(sub => sub.Id));
        Assert.Equal(NodeSubIds(first), NodeSubIds(second));
    }

    // The comment carry rebuilds every resource it matches, inline at any depth or kept in the table, and every one keeps
    // the id the carry started from: the id of the .tscn's sub_resource of its class.
    [Fact]
    public void IdsFindEveryResourceAfterTheCommentCarry()
    {
        const string text = $$"""
            <Node2D {{X}} Name="Root">
              <x:Resources>
                <CanvasItemMaterial x:Key="Shared" LightMode="Unshaded" />
                <ShaderMaterial x:Key="Shaded"><ShaderMaterial.Shader><Shader /></ShaderMaterial.Shader></ShaderMaterial>
              </x:Resources>
              <Sprite2D Name="One" Material="{Shared}">
                <Sprite2D.Texture>
                  <GradientTexture2D Width="8">
                    <GradientTexture2D.Gradient><Gradient /></GradientTexture2D.Gradient>
                  </GradientTexture2D>
                </Sprite2D.Texture>
              </Sprite2D>
              <Sprite2D Name="Two" Material="{Shared}" />
              <Sprite2D Name="Three" Material="{Shaded}" />
              <Sprite2D Name="Four" Material="{Shaded}" />
            </Node2D>
            """;
        XscnDocument previous = Parse(text);
        TscnDocument saved = Convert(previous).Document;

        ConversionResult<XscnDocument> forward = TscnToXscn.Convert(saved, Metadata, Options with { Previous = previous });

        Assert.Equal(["Shared", "Shaded"], forward.Document.Resources.Select(entry => entry.Key));
        ConversionIds ids = Assert.IsType<ConversionIds>(forward.Ids);
        XscnResource[] resources = [.. AllResources(forward.Document)];
        Assert.Equal(5, resources.Length);

        // Each class is on one resource only, so the .tscn's sub_resource whose type is that class is the one it must get.
        var savedIds = saved.SubResources.ToDictionary(sub => sub.Type, sub => sub.Id);
        Assert.Equal(5, savedIds.Count);
        Assert.All(resources, resource => Assert.Equal(savedIds[Assert.IsType<XscnClassType>(resource.Type).Class.Name], ids.SubIdOf(resource)));
        Assert.Equal(5, resources.Select(resource => ids.SubIdOf(resource)).Distinct(StringComparer.Ordinal).Count());
    }

    // Every resource a document holds: the table's entries and each resource held inline, at any depth.
    private static IEnumerable<XscnResource> AllResources(XscnDocument document) =>
        document.Resources.SelectMany(entry => HeldResources(entry).Prepend(entry)).Concat(HeldResources(document.Root));

    private static IEnumerable<XscnResource> HeldResources(XscnElement element)
    {
        IEnumerable<XscnProperty> properties = element switch
        {
            XscnNodeElement node => node.Properties,
            XscnResource resource => resource.Properties,
            _ => [],
        };
        IEnumerable<XscnResource> inline = properties
            .Select(property => property.Value)
            .OfType<XscnInline>()
            .SelectMany(value => HeldResources(value.Resource).Prepend(value.Resource));
        return element is XscnNodeElement { Children: var children } ? inline.Concat(children.SelectMany(HeldResources)) : inline;
    }

    // Two places given one seed mint one id first; the second takes the next one free.
    [Fact]
    public void IdsStayUniqueOnACollision()
    {
        const string text = $$"""
            <Node2D {{X}} Name="Root">
              <Sprite2D Name="One">
                <Sprite2D.Material><CanvasItemMaterial LightMode="Unshaded" /></Sprite2D.Material>
              </Sprite2D>
              <Sprite2D Name="Two">
                <Sprite2D.Material><CanvasItemMaterial BlendMode="Add" /></Sprite2D.Material>
              </Sprite2D>
            </Node2D>
            """;

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Parse(text), Metadata, Options, _ => 1);

        Assert.Empty(back.Issues);
        Dictionary<string, string> ids = NodeSubIds(back.Document);
        Assert.Equal(2, ids.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ids.Values.Order(StringComparer.Ordinal), back.Document.SubResources.Select(sub => sub.Id).Order(StringComparer.Ordinal));
    }

    // The sub_resource id each node property names, keyed by "<parent>/<node>|<property>".
    private static Dictionary<string, string> NodeSubIds(TscnDocument scene)
    {
        Dictionary<string, string> ids = new(StringComparer.Ordinal);
        foreach (TscnNode node in scene.Nodes)
        {
            string path =
                node.Parent is null ? node.Name
                : node.Parent == "." ? "Root/" + node.Name
                : $"Root/{node.Parent}/{node.Name}";
            foreach (TscnProperty property in node.Properties)
            {
                if (property.Value is GodotResourceRef { Kind: GodotRefKind.Sub } sub)
                {
                    ids.Add(path + "|" + property.Name, sub.Id);
                }
            }
        }

        return ids;
    }

    private static TscnDocument Back(string text) => Convert(Parse(text)).Document;

    private static ConversionResult<TscnDocument> Convert(XscnDocument document)
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, Metadata, Options);
        Assert.Empty(back.Issues);
        return back;
    }

    private static XscnDocument Parse(string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata);
        Assert.Empty(parsed.Diagnostics);
        return parsed.Document;
    }

    private static string ReadGolden(string file) =>
        new UTF8Encoding(false, true).GetString(File.ReadAllBytes(RepoFiles.PathOf("tests", "fixtures", "converted", file)));
}
