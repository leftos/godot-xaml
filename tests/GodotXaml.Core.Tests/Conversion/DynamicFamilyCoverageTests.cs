using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using static GodotXaml.Core.Conversion.DynamicPropertyFamilies;

namespace GodotXaml.Core.Tests.Conversion;

// Every row of the dynamic-family table is exercised by a fixture under tests/fixtures/scenes, or carries a reviewed reason
// no fixture can hold it; a reason whose row is gone or now covered fails too, so the list never outlives its rows.
public sealed class DynamicFamilyCoverageTests
{
    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    // Rows no fixture can hold, keyed by class and the pattern as the table writes it, so a pattern edit forces a review; each
    // reason is read in Godot 4.7.2.
    private static readonly Dictionary<(string Class, string Pattern), string> Exceptions = new()
    {
        // Editor-only or never stored.
        [("ArrayMesh", @"^surface_[0-9]+/")] = "ArrayMesh::_get_property_list lists each surface's names with PROPERTY_USAGE_EDITOR only.",
        [("Curve", @"^point_[0-9]+/")] = "Curve::_bind_methods registers the points with PROPERTY_USAGE_EDITOR only; it stores them as _data.",
        [("Curve2D", @"^point_[0-9]+/")] = "Curve2D::_bind_methods registers the points with PROPERTY_USAGE_EDITOR only; it stores them as _data.",
        [("Curve3D", @"^point_[0-9]+/")] = "Curve3D::_bind_methods registers the points with PROPERTY_USAGE_EDITOR only; it stores them as _data.",
        [("MultiplayerSpawner", @"^_spawnable_scene_count\z")] =
            "MultiplayerSpawner::_get_property_list lists it with PROPERTY_USAGE_EDITOR; the stored form is _spawnable_scenes.",
        [("MultiplayerSpawner", @"^scenes/[0-9]+\z")] =
            "MultiplayerSpawner::_get_property_list lists each scene with PROPERTY_USAGE_EDITOR; the stored form is _spawnable_scenes.",
        [("SkeletonModification2DCCDIK", @"^editor/draw_gizmo\z")] =
            "Listed only under TOOLS_ENABLED and Engine::is_editor_hint(), which the generator's --headless --script run is not.",
        [("SkeletonModification2DLookAt", @"^editor/draw_gizmo\z")] =
            "Listed only under TOOLS_ENABLED and Engine::is_editor_hint(), which the generator's --headless --script run is not.",
        [("SkeletonModification2DStackHolder", @"^editor/draw_gizmo\z")] =
            "Listed only under TOOLS_ENABLED and Engine::is_editor_hint(), which the generator's --headless --script run is not.",
        [("SkeletonModification2DTwoBoneIK", @"^editor/draw_gizmo\z")] =
            "Listed only under TOOLS_ENABLED and Engine::is_editor_hint(), which the generator's --headless --script run is not.",
        [("SkeletonModification2DTwoBoneIK", @"^editor/draw_min_max\z")] =
            "Listed only under TOOLS_ENABLED and Engine::is_editor_hint(), which the generator's --headless --script run is not.",
        [("SkeletonModification2DPhysicalBones", @"^fetch_bones\z")] =
            "Listed only under Engine::is_editor_hint(), which the generator's --headless --script run is not.",
        [("VisualShader", @"^preview_params/")] =
            "VisualShader::_set accepts a preview parameter only under Engine::is_editor_hint(), so a script run stores none.",

        // Derived.
        [("LimitAngularVelocityModifier3D", @"^joints/[0-9]+/")] =
            "LimitAngularVelocityModifier3D::_get_property_list lists the joints as read-only, without PROPERTY_USAGE_STORAGE.",

        // Never a section: TileData is an Object, not a Resource; its names are stored on TileSetAtlasSource's tiles.
        [("TileData", @"^occlusion_layer_[0-9]+/")] = "TileData is not a Resource, so no fixture has a TileData section.",
        [("TileData", @"^physics_layer_[0-9]+/")] = "TileData is not a Resource, so no fixture has a TileData section.",
        [("TileData", @"^terrains_peering_bit/")] = "TileData is not a Resource, so no fixture has a TileData section.",
        [("TileData", @"^navigation_layer_[0-9]+/")] = "TileData is not a Resource, so no fixture has a TileData section.",
        [("TileData", @"^custom_data_[0-9]+\z")] = "TileData is not a Resource, so no fixture has a TileData section.",

        // Shadowed: an earlier row of the class ranks every name Godot writes, so the row holds only names Godot never saves.
        [("IterateIK3D", @"^settings/[0-9]+/")] =
            "Unlisted key: the rows above rank every settings/N/ name ChainIK3D and IterateIK3D list, joints/M/ included.",
        [("VisualShader", @"^modes/")] = "Unlisted render mode: every mode with options ShaderTypes::ShaderTypes registers has its own row above.",
        [("VisualShader", @"^nodes/[^/]+/(?<index>[0-9]+)/(?<key>[^/]*)")] =
            "Unlisted graph type: VisualShader::_get_property_list writes only the type_string types, each with its own row.",
        [("VisualShader", @"^nodes/")] =
            "Unlisted graph name: every nodes/ name VisualShader::_get_property_list writes ranks by a per-type row above.",
    };

    [Fact]
    public void EveryFamilyRowIsCoveredByAFixtureOrAnException()
    {
        HashSet<(string Class, string Pattern)> covered = CoveredRows();
        List<string> uncovered =
        [
            .. All.Select(row => (row.Class, Pattern: row.Family.Pattern.ToString()))
                .Where(row => !covered.Contains(row) && !Exceptions.ContainsKey(row))
                .Select(row => $"{row.Class} {row.Pattern}"),
        ];

        Assert.True(uncovered.Count == 0, "Uncovered rows:\n" + string.Join('\n', uncovered));
    }

    [Fact]
    public void NoExceptionIsStale()
    {
        HashSet<(string Class, string Pattern)> rows = [.. All.Select(row => (row.Class, row.Family.Pattern.ToString()))];
        HashSet<(string Class, string Pattern)> covered = CoveredRows();
        List<string> stale =
        [
            .. Exceptions
                .Keys.Where(row => !rows.Contains(row) || covered.Contains(row))
                .Select(row => $"{row.Class} {row.Pattern}: {(rows.Contains(row) ? "covered by a fixture" : "no such row")}"),
        ];

        Assert.True(stale.Count == 0, "Stale exceptions:\n" + string.Join('\n', stale));
    }

    // The rows a fixture's property name ranks by: each section with a class (the main resource, each sub-resource, each
    // node with a type) gives its names to the first family of its chain, base first, that matches, as PropertyOrder ranks.
    private static HashSet<(string Class, string Pattern)> CoveredRows()
    {
        HashSet<(string Class, string Pattern)> covered = [];
        string folder = RepoFiles.PathOf("tests", "fixtures", "scenes");
        IEnumerable<string> files = Directory
            .EnumerateFiles(folder, "*.tscn")
            .Concat(Directory.EnumerateFiles(folder, "*.tres"))
            .Order(StringComparer.Ordinal);
        foreach (string file in files)
        {
            foreach ((string? type, IEnumerable<TscnProperty> properties) in SectionsOf(TscnReader.Read(File.ReadAllText(file))))
            {
                if (type is not null && Metadata.FindClass(type) is { } cls)
                {
                    AddCovered(covered, cls, properties);
                }
            }
        }

        return covered;
    }

    private static IEnumerable<(string? Type, IEnumerable<TscnProperty> Properties)> SectionsOf(TscnDocument document) =>
        [
            (document.Header.Type, document.ResourceProperties),
            .. document.SubResources.Select(sub => ((string?)sub.Type, (IEnumerable<TscnProperty>)sub.Properties)),
            .. document.Nodes.Select(node => (node.Type, (IEnumerable<TscnProperty>)node.Properties)),
        ];

    private static void AddCovered(HashSet<(string Class, string Pattern)> covered, GodotClassInfo cls, IEnumerable<TscnProperty> properties)
    {
        List<GodotClassInfo> chain = [.. Metadata.ChainOf(cls).Reverse()];
        List<(string Class, DynamicFamily Family)> families =
        [
            .. chain.SelectMany(declaring => Of(declaring.Name).Select(family => (declaring.Name, family))),
        ];
        foreach (string name in properties.Select(property => property.Name))
        {
            if (chain.Any(declaring => declaring.ListOrder.Contains(name, StringComparer.Ordinal)))
            {
                continue;
            }

            foreach ((string declaring, DynamicFamily family) in families)
            {
                if (family.Pattern.IsMatch(name))
                {
                    covered.Add((declaring, family.Pattern.ToString()));
                    break;
                }
            }
        }
    }
}
