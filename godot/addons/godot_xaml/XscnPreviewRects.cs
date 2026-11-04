using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Addon;

/// <summary>A Control of the preview tree: where it lies and which element of the text made it.</summary>
/// <param name="Path">The node's path from the scene root: "." for the root, "Box/Ok" below it.</param>
/// <param name="Class">The node's engine class (<c>get_class</c>).</param>
/// <param name="Kind">
/// What the rectangle is of: <see cref="XscnPreviewRects.ControlKind"/> in this version, so 2D and 3D entries can follow.
/// </param>
/// <param name="Rect">The Control's global rectangle (<c>Control.get_global_rect</c>).</param>
/// <param name="SpanStart">The UTF-16 offset in the text of the element's <c>&lt;</c>.</param>
/// <param name="SpanLength">The element's length in UTF-16 units, to the end of its end tag.</param>
internal sealed record PreviewRect(string Path, string Class, string Kind, Rect2 Rect, int SpanStart, int SpanLength);

/// <summary>
/// The rectangles of an instantiated preview's Controls, back to front, each tagged with its element's source span
/// (<see cref="XscnElement.Span"/>).
/// Back to front is the tree's pre-order: a parent before its children, siblings in child order. That is Godot's draw
/// order for CanvasItems without <c>z_index</c>, <c>top_level</c>, <c>show_behind_parent</c>, <c>y_sort_enabled</c> or
/// CanvasLayers, which this version does not model.
/// Only Controls get entries; a non-Control node's Control descendants still do. A Control hidden in the tree
/// (<c>is_visible_in_tree</c>) gets none, nor do its descendants, since Godot does not draw them
/// (servers/rendering/renderer_canvas_cull.cpp). The walk does not enter a Viewport below the root (a SubViewport, a
/// Window): a rectangle there is in that viewport's space. A node inside an instanced scene has no
/// element of its own, so it takes the instance's: each node takes the element of its path's longest mapped prefix.
/// Main thread only: it calls Godot.
/// </summary>
internal static class XscnPreviewRects
{
    /// <summary>The kind of a Control's entry.</summary>
    internal const string ControlKind = "control";

    private const string RootPath = ".";

    /// <summary>Collects the rectangle and source span of each Control under <paramref name="root"/>, back to front.</summary>
    /// <param name="root">The preview's root, inside a tree (such as a SubViewport) whose layout has run.</param>
    /// <param name="document">The document the root was instantiated from.</param>
    /// <returns>One entry per Control, the root's included when it is one, in pre-order.</returns>
    /// <exception cref="InvalidOperationException">
    /// The root is outside the tree: a Container sorts its children only inside it (<c>Container::queue_sort</c>,
    /// scene/gui/container.cpp), so its rectangles would be no layout's.
    /// </exception>
    internal static ImmutableArray<PreviewRect> Collect(Node root, XscnDocument document)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(document);
        if (!root.IsInsideTree())
        {
            throw new InvalidOperationException(
                "The preview root is not inside a tree, so it has no layout: add the preview root to a tree, such as a "
                    + "SubViewport, before collecting its rectangles."
            );
        }

        var elements = new Dictionary<string, XscnElement>(StringComparer.Ordinal) { [RootPath] = document.Root };
        if (document.Root is XscnNodeElement rootElement)
        {
            AddChildren(rootElement, "", elements);
        }

        ImmutableArray<PreviewRect>.Builder rects = ImmutableArray.CreateBuilder<PreviewRect>();
        Walk(root, root, elements, rects);
        return rects.ToImmutable();
    }

    // Maps each node element under the parent to its path, the parent's path given as a prefix ending in "/" (empty for
    // the root). An override is never a target, but the nodes it adds lie under its path inside the instance.
    private static void AddChildren(XscnNodeElement parent, string prefix, Dictionary<string, XscnElement> elements)
    {
        foreach (XscnNodeElement child in parent.Children)
        {
            string path = child switch
            {
                XscnNode node => prefix + node.Name,
                XscnOverride over => XscnPreviewScene.Simplified(prefix + over.Path),
                _ => throw new InvalidOperationException($"Unexpected element {child.GetType().Name} under {prefix}."),
            };
            if (child is XscnNode)
            {
                elements[path] = child;
            }

            AddChildren(child, path + "/", elements);
        }
    }

    // Adds the node's entry when it is a Control, then its children's, in child order. Internal children (a ScrollContainer's
    // bars) are left out: no element of the text made them. A hidden CanvasItem and a Viewport below the root end the walk
    // on their branch.
    private static void Walk(Node root, Node node, Dictionary<string, XscnElement> elements, ImmutableArray<PreviewRect>.Builder rects)
    {
        if ((node is CanvasItem item && !item.IsVisibleInTree()) || (node != root && node is Viewport))
        {
            return;
        }

        if (node is Control control)
        {
            string path;
            using (NodePath nodePath = root.GetPathTo(node))
            {
                path = nodePath.ToString();
            }

            TextSpanOf(path, elements, out int start, out int length);
            rects.Add(new PreviewRect(path, node.GetClass(), ControlKind, control.GetGlobalRect(), start, length));
        }

        int count = node.GetChildCount();
        for (int index = 0; index < count; index++)
        {
            Walk(root, node.GetChild(index), elements, rects);
        }
    }

    // The span of the element of the path's longest mapped prefix, at a "/" boundary or the whole path; the root's path,
    // ".", is always mapped, so the loop ends there at the latest.
    private static void TextSpanOf(string path, Dictionary<string, XscnElement> elements, out int start, out int length)
    {
        string candidate = path;
        while (!elements.ContainsKey(candidate))
        {
            int slash = candidate.LastIndexOf('/');
            candidate = slash < 0 ? RootPath : candidate[..slash];
        }

        XscnElement element = elements[candidate];
        start = element.Span.Start;
        length = element.Span.Length;
    }
}
