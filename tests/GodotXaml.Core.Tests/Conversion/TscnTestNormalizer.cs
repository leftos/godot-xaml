using System.Collections.Immutable;
using System.Globalization;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Conversion;

/// <summary>
/// Rewrites a .tscn or .tres model so that two files Godot would load as the same scene compare equal as text:
/// Godot mints section ids per save, and the .xscn keeps properties in another order than Godot's property list.
/// </summary>
internal static class TscnTestNormalizer
{
    /// <summary>
    /// Renumbers the ext_resource ids <c>1</c>, <c>2</c>, … and the sub_resource ids <c>&lt;Type&gt;_&lt;n&gt;</c>, both
    /// in section order (n counts every sub_resource from 1), and rewrites every <c>ExtResource</c> and
    /// <c>SubResource</c> reference in node, sub_resource, <c>[resource]</c> and connection values and in each node's
    /// <c>instance=</c>. Section and property order are left as they are; a reference to an id no section has is kept.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The document with its ids renumbered.</returns>
    public static TscnDocument Normalize(TscnDocument document)
    {
        Dictionary<string, string> extIds = new(StringComparer.Ordinal);
        foreach (TscnExtResource ext in document.ExtResources)
        {
            extIds.TryAdd(ext.Id, (extIds.Count + 1).ToString(CultureInfo.InvariantCulture));
        }

        Dictionary<string, string> subIds = new(StringComparer.Ordinal);
        for (int i = 0; i < document.SubResources.Length; i++)
        {
            TscnSubResource sub = document.SubResources[i];
            subIds.TryAdd(sub.Id, sub.Type + "_" + (i + 1).ToString(CultureInfo.InvariantCulture));
        }

        GodotValue Rewrite(GodotValue value) => ValueReferences.Map(value, reference => Renamed(reference, extIds, subIds));

        ImmutableArray<TscnProperty> Properties(ImmutableArray<TscnProperty> properties) =>
            [.. properties.Select(property => property with { Value = Rewrite(property.Value) })];

        return document with
        {
            ExtResources = [.. document.ExtResources.Select(ext => ext with { Id = extIds.GetValueOrDefault(ext.Id, ext.Id) })],
            SubResources =
            [
                .. document.SubResources.Select(sub =>
                    sub with
                    {
                        Id = subIds.GetValueOrDefault(sub.Id, sub.Id),
                        Properties = Properties(sub.Properties),
                    }
                ),
            ],
            ResourceProperties = Properties(document.ResourceProperties),
            Nodes =
            [
                .. document.Nodes.Select(node =>
                    node with
                    {
                        Instance = node.Instance is { } instance ? Rewrite(instance) : null,
                        Properties = Properties(node.Properties),
                    }
                ),
            ],
            Connections = [.. document.Connections.Select(connection => connection with { Binds = [.. connection.Binds.Select(Rewrite)] })],
        };
    }

    /// <summary>
    /// Sorts the properties of every node and sub_resource by name (ordinal), for comparing a model whose property order
    /// is the .xscn's alphabetical one with a file in Godot's property-list order.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The document with its node and sub_resource properties sorted.</returns>
    public static TscnDocument SortProperties(TscnDocument document) =>
        document with
        {
            SubResources = [.. document.SubResources.Select(sub => sub with { Properties = Sorted(sub.Properties) })],
            Nodes = [.. document.Nodes.Select(node => node with { Properties = Sorted(node.Properties) })],
        };

    private static ImmutableArray<TscnProperty> Sorted(ImmutableArray<TscnProperty> properties) =>
        [.. properties.OrderBy(property => property.Name, StringComparer.Ordinal)];

    private static GodotValue Renamed(GodotValue reference, Dictionary<string, string> extIds, Dictionary<string, string> subIds) =>
        reference switch
        {
            GodotResourceRef { Kind: GodotRefKind.External } ext when extIds.TryGetValue(ext.Id, out string? id) => ext with { Id = id },
            GodotResourceRef { Kind: GodotRefKind.Sub } sub when subIds.TryGetValue(sub.Id, out string? id) => sub with { Id = id },
            _ => reference,
        };
}
