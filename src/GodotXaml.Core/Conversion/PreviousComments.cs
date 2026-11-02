using System.Collections.Immutable;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// Carries a previous document's comments onto the parts of a freshly converted document that match it, so a save that
/// goes through <c>.tscn</c> text keeps them (docs/DECISIONS.md, "Canonical formatting on save"). A node element matches
/// by its path from the root, an <c>x:Resources</c> entry by the key the conversion kept for it, a property by its Godot
/// name within a matched element, a connection by its signal, target and method, and a uid by its path. A part of the new
/// document with no match takes no comments, a document of the other kind takes none at all, and the new document's
/// structure, order and values are never changed.
/// </summary>
internal static class PreviousComments
{
    /// <summary>The converted document, its matching comment slots filled from the document the conversion replaces.</summary>
    /// <param name="previous">The <c>.xscn</c> or <c>.xres</c> the conversion replaces.</param>
    /// <param name="next">The converted document, without comments.</param>
    /// <param name="keptKeys">
    /// The <c>x:Resources</c> keys <paramref name="previous"/> gave the sub_resources that matched its entries; an entry
    /// this conversion makes takes comments only when its key is one of them.
    /// </param>
    /// <returns>A copy of <paramref name="next"/> carrying the comments of the parts of <paramref name="previous"/> it matches.</returns>
    public static XscnDocument Carry(XscnDocument previous, XscnDocument next, IReadOnlySet<string> keptKeys)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(keptKeys);
        if (previous.Kind != next.Kind)
        {
            return next;
        }

        var index = new Index(previous);
        bool uids = !next.Uids.IsEmpty;
        bool resources = !next.Resources.IsEmpty;
        return next with
        {
            Uids = uids ? CarryUids(previous.Uids, next.Uids) : next.Uids,
            UidsLeadingComments = uids ? previous.UidsLeadingComments : [],
            UidsTrailingComments = uids ? previous.UidsTrailingComments : [],
            Resources = CarryResources(index, keptKeys, next.Resources),
            ResourcesLeadingComments = resources ? previous.ResourcesLeadingComments : [],
            ResourcesTrailingComments = resources ? previous.ResourcesTrailingComments : [],
            Root = CarryRoot(index, next.Root),
        };
    }

    // The root: the root element of either kind of document, matched as the element at the root's path.
    private static XscnElement CarryRoot(Index index, XscnElement next) =>
        (index.Root, next) switch
        {
            (XscnNodeElement previous, XscnNodeElement node) => CarryNode(previous, TscnNodePaths.Root, index, node),
            (XscnResource previous, XscnResource resource) => CarryResource(previous, index, resource),
            _ => next,
        };

    // A node element and its subtree: its children take their comments from the previous element at the same path.
    private static XscnNodeElement CarryNode(XscnNodeElement? previous, string path, Index index, XscnNodeElement next)
    {
        var carried = (XscnNodeElement)CarryComments(previous, next);
        return carried with
        {
            Properties = CarryProperties(previous?.Properties ?? [], index, next.Properties),
            Connections = CarryConnections(previous?.Connections ?? [], next.Connections),
            Children = [.. next.Children.Select(child => CarryChild(path, index, child))],
        };
    }

    private static XscnNodeElement CarryChild(string parent, Index index, XscnNodeElement next)
    {
        string path = ChildPath(parent, next);
        return CarryNode(index.Nodes.GetValueOrDefault(path), path, index, next);
    }

    // A child's path from its parent's: a node's name, or an override's path inside the instance it belongs to.
    private static string ChildPath(string parent, XscnNodeElement child)
    {
        string relative = child switch
        {
            XscnNode node => node.Name,
            XscnOverride change => change.Path,
            _ => throw new ArgumentException($"A {child.GetType().Name} is not a child of a node element.", nameof(child)),
        };
        return parent == TscnNodePaths.Root ? relative : parent + "/" + relative;
    }

    private static XscnResource CarryResource(XscnResource? previous, Index index, XscnResource next)
    {
        var carried = (XscnResource)CarryComments(previous, next);
        return carried with { Properties = CarryProperties(previous?.Properties ?? [], index, next.Properties) };
    }

    // The x:Resources table: an entry takes comments only when the forward conversion kept its key from the previous
    // document's entries, so a key made from scratch that happens to equal a deleted entry's takes nothing.
    private static ImmutableArray<XscnResource> CarryResources(Index index, IReadOnlySet<string> keptKeys, ImmutableArray<XscnResource> next) =>
        [
            .. next.Select(entry =>
                entry.Key is { } key && keptKeys.Contains(key) ? CarryResource(index.Resources.GetValueOrDefault(key), index, entry) : entry
            ),
        ];

    // A part with no match loses its comments: what they described is gone.
    private static XscnElement CarryComments(XscnElement? previous, XscnElement next) =>
        previous is null ? next : next with { LeadingComments = previous.LeadingComments, TrailingComments = previous.TrailingComments };

    // A matched pair of properties: any matched property takes the leading comments, but only one whose value still holds
    // an inline resource of the same class takes the trailing ones and the comments of that resource, since a property's
    // trailing comments belong to the resource it holds and a value of another shape cannot keep them.
    private static XscnProperty CarryValue(XscnProperty previous, Index index, XscnProperty next)
    {
        XscnProperty matched = next with { LeadingComments = previous.LeadingComments };
        return next.Value is XscnInline inline && previous.Value is XscnInline earlier && SameClass(earlier.Resource.Type, inline.Resource.Type)
            ? matched with
            {
                TrailingComments = previous.TrailingComments,
                Value = new XscnInline(CarryResource(earlier.Resource, index, inline.Resource)),
            }
            : matched;
    }

    private static bool SameClass(XscnType previous, XscnType next) =>
        (previous, next) switch
        {
            (XscnClassType left, XscnClassType right) => string.Equals(left.Class.Name, right.Class.Name, StringComparison.Ordinal),
            (XscnUnknownType left, XscnUnknownType right) => string.Equals(left.Name, right.Name, StringComparison.Ordinal),
            _ => false,
        };

    // Within a matched element, a property matched by its Godot name, ordinal among the properties that share one.
    private static ImmutableArray<XscnProperty> CarryProperties(ImmutableArray<XscnProperty> previous, Index index, ImmutableArray<XscnProperty> next)
    {
        Dictionary<string, Queue<XscnProperty>> pending = Queued(previous, property => property.GodotName);
        return [.. next.Select(property => Take(pending, property.GodotName) is { } source ? CarryValue(source, index, property) : property)];
    }

    // Within a matched element, by signal, target and method, several with the same triple in their order.
    private static ImmutableArray<XscnConnection> CarryConnections(ImmutableArray<XscnConnection> previous, ImmutableArray<XscnConnection> next)
    {
        Dictionary<(string, string, string), Queue<XscnConnection>> pending = Queued(
            previous,
            connection => (connection.Signal, connection.To, connection.Method)
        );
        return
        [
            .. next.Select(connection =>
                Take(pending, (connection.Signal, connection.To, connection.Method)) is { } source
                    ? connection with
                    {
                        LeadingComments = source.LeadingComments,
                    }
                    : connection
            ),
        ];
    }

    // The x:Uids entries, by path, ordinal among those with the same path.
    private static ImmutableArray<XscnUid> CarryUids(ImmutableArray<XscnUid> previous, ImmutableArray<XscnUid> next)
    {
        Dictionary<string, Queue<XscnUid>> pending = Queued(previous, uid => uid.Path);
        return [.. next.Select(uid => Take(pending, uid.Path) is { } source ? uid with { LeadingComments = source.LeadingComments } : uid)];
    }

    // The items by key, in their order, so that the nth of a key in the new document takes the nth in the previous one.
    private static Dictionary<TKey, Queue<TItem>> Queued<TItem, TKey>(IEnumerable<TItem> items, Func<TItem, TKey> key)
        where TItem : class
        where TKey : notnull
    {
        Dictionary<TKey, Queue<TItem>> queues = [];
        foreach (TItem item in items)
        {
            TKey name = key(item);
            if (!queues.TryGetValue(name, out Queue<TItem>? queue))
            {
                queue = new Queue<TItem>();
                queues[name] = queue;
            }

            queue.Enqueue(item);
        }

        return queues;
    }

    // The next item queued under a key, or null when the previous document had none there.
    private static TItem? Take<TItem, TKey>(Dictionary<TKey, Queue<TItem>> pending, TKey key)
        where TItem : class
        where TKey : notnull => pending.TryGetValue(key, out Queue<TItem>? queue) && queue.Count > 0 ? queue.Dequeue() : null;

    /// <summary>What a previous document offers a match: its root, its node elements by path, its table entries by key.</summary>
    private sealed class Index
    {
        public Index(XscnDocument previous)
        {
            Root = previous.Root;
            if (previous.Root is XscnNodeElement root)
            {
                Collect(root, TscnNodePaths.Root, Nodes);
            }

            foreach (XscnResource entry in previous.Resources)
            {
                if (entry.Key is { } key)
                {
                    Resources.TryAdd(key, entry);
                }
            }
        }

        /// <summary>The previous document's root element.</summary>
        public XscnElement Root { get; }

        /// <summary>Its node elements, by their path from the root.</summary>
        public Dictionary<string, XscnNodeElement> Nodes { get; } = new(StringComparer.Ordinal);

        /// <summary>Its <c>x:Resources</c> entries, by key.</summary>
        public Dictionary<string, XscnResource> Resources { get; } = new(StringComparer.Ordinal);

        private static void Collect(XscnNodeElement element, string path, Dictionary<string, XscnNodeElement> nodes)
        {
            if (!nodes.TryAdd(path, element))
            {
                return;
            }

            foreach (XscnNodeElement child in element.Children)
            {
                Collect(child, ChildPath(path, child), nodes);
            }
        }
    }
}
