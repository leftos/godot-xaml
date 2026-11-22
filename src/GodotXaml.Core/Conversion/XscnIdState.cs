using System.Collections.Immutable;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// The id state of a <c>.xscn</c> or <c>.xres</c> a save replaces, which the save keeps: whether it has an <c>x:Uids</c>
/// table, and whether any of its nodes has an <c>x:UniqueId</c> (<see cref="ConversionOptions.WriteUidTable"/>,
/// <see cref="ConversionOptions.WriteNodeIds"/>).
/// </summary>
public static class XscnIdState
{
    /// <summary>Whether the document has an <c>x:Uids</c> table: one with an entry, or one that holds only comments.</summary>
    /// <param name="document">The document a save replaces.</param>
    /// <returns>True when the table has an entry or a comment; false for no table, or an empty one with no comment.</returns>
    public static bool HasUidTable(XscnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return !document.Uids.IsEmpty || !document.UidsLeadingComments.IsEmpty || !document.UidsTrailingComments.IsEmpty;
    }

    /// <summary>
    /// Whether any node of the document has an <c>x:UniqueId</c>: a node, an instance or an <c>x:Override</c> under the root,
    /// or one in the scene a <c>PackedScene</c> holds, in <c>x:Resources</c>, <c>x:Scene</c> or an inline resource.
    /// </summary>
    /// <param name="document">The document a save replaces.</param>
    /// <returns>True when at least one node carries an id.</returns>
    public static bool HasNodeIds(XscnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Resources.Any(HoldsIds) || (document.Scene is { } scene && HoldsIds(scene)) || HoldsIds(document.Root);
    }

    // Walked as the writer walks a document for scene roots, with the scene roots' nodes themselves.
    private static bool HoldsIds(XscnElement element) =>
        element switch
        {
            XscnResource resource => (resource.SceneRoot is { } root && HoldsIds(root)) || HoldsIds(resource.Properties),
            XscnNodeElement node => node.UniqueId is not null || HoldsIds(node.Properties) || node.Children.Any(HoldsIds),
            _ => false,
        };

    private static bool HoldsIds(ImmutableArray<XscnProperty> properties) =>
        properties.Any(p => p.Value is XscnInline inline && HoldsIds(inline.Resource));
}
