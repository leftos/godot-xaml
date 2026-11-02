using System.Collections.Immutable;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// Carries a previous document's comments onto the parts of a freshly converted document that match it, so a save that
/// goes through <c>.tscn</c> text keeps them (docs/DECISIONS.md, "Canonical formatting on save"). A node element matches
/// by its unique id first, when the id occurs once in both documents, and otherwise by its path from the root; an
/// <c>x:Resources</c> entry matches by the key the conversion kept for it, a property by its Godot name within a matched
/// element, a connection by its signal, target and method, and a uid by its path. A part of the new document with no match
/// takes no comments, a document of the other kind takes none at all, and every comment the new document cannot carry is
/// reported as a <see cref="ConversionCodes.CommentDropped"/> warning, naming the part by its place in the previous
/// document.
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
    /// <param name="issues">The conversion's issues; a comment the result cannot carry adds a warning here.</param>
    /// <returns>
    /// A copy of <paramref name="next"/> carrying the comments of the parts of <paramref name="previous"/> it matches, and
    /// each resource of <paramref name="next"/> the carry rebuilt, by reference, to the copy the returned document holds.
    /// </returns>
    public static (XscnDocument Document, IReadOnlyDictionary<XscnResource, XscnResource> Copies) Carry(
        XscnDocument previous,
        XscnDocument next,
        IReadOnlySet<string> keptKeys,
        List<ConversionIssue> issues
    )
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(keptKeys);
        ArgumentNullException.ThrowIfNull(issues);
        var carrier = new Carrier(previous, next, keptKeys, issues);
        XscnDocument carried = carrier.Run();
        return (carried, carrier.Copies);
    }

    /// <summary>One save's carrying and reporting, over one previous document and the one it becomes.</summary>
    private sealed class Carrier
    {
        private const string NoLongerInScene = "it is no longer in the scene";
        private const string NoLongerInline = "its value is no longer an inline resource";

        // The characters of a dropped comment's first line that a warning quotes, before the ellipsis.
        private const int QuotedLength = 60;

        private readonly XscnDocument previous;
        private readonly XscnDocument next;
        private readonly IReadOnlySet<string> keptKeys;
        private readonly List<ConversionIssue> issues;
        private readonly Index index;

        // The previous node elements already matched, by unique id or by path, so each is matched at most once.
        private readonly HashSet<XscnNodeElement> used = new(ReferenceEqualityComparer.Instance);

        // The new node elements matched to a previous one by a unique id that occurs once in each document.
        private readonly Dictionary<XscnNodeElement, XscnNodeElement> byId;

        // Each resource of the new document the carry rebuilt, to its copy.
        private readonly Dictionary<XscnResource, XscnResource> copies = new(ReferenceEqualityComparer.Instance);

        public Carrier(XscnDocument previous, XscnDocument next, IReadOnlySet<string> keptKeys, List<ConversionIssue> issues)
        {
            this.previous = previous;
            this.next = next;
            this.keptKeys = keptKeys;
            this.issues = issues;
            index = new Index(previous);
            byId = UniqueIdMatches([.. NodeElements(previous.Root)], [.. NodeElements(next.Root)]);
            used.UnionWith(byId.Values);
            if (previous.Kind == next.Kind && previous.Root is XscnNodeElement root)
            {
                used.Add(root);
            }
        }

        public IReadOnlyDictionary<XscnResource, XscnResource> Copies => copies;

        public XscnDocument Run()
        {
            if (previous.Kind != next.Kind)
            {
                ReportReplaced();
                return next;
            }

            bool uids = !next.Uids.IsEmpty;
            bool resources = !next.Resources.IsEmpty;
            XscnDocument carried = next with
            {
                Uids = CarryUids(previous.Uids, next.Uids),
                UidsLeadingComments = uids ? previous.UidsLeadingComments : [],
                UidsTrailingComments = uids ? previous.UidsTrailingComments : [],
                Resources = CarryResources(next.Resources),
                ResourcesLeadingComments = resources ? previous.ResourcesLeadingComments : [],
                ResourcesTrailingComments = resources ? previous.ResourcesTrailingComments : [],
                Root = CarryRoot(next.Root),
            };
            ReportUnmatched(NoLongerInScene);
            if (!uids)
            {
                Report("the x:Uids table", "x:Uids", [.. previous.UidsLeadingComments, .. previous.UidsTrailingComments], NoLongerInScene);
            }

            if (!resources)
            {
                Report(
                    "the x:Resources table",
                    "x:Resources",
                    [.. previous.ResourcesLeadingComments, .. previous.ResourcesTrailingComments],
                    NoLongerInScene
                );
            }

            return carried;
        }

        // The root: the root element of either kind of document, matched as the element at the root's path.
        private XscnElement CarryRoot(XscnElement nextRoot) =>
            (previous.Root, nextRoot) switch
            {
                (XscnNodeElement before, XscnNodeElement after) => CarryNode(before, TscnNodePaths.Root, after),
                (XscnResource before, XscnResource after) => CarryResource(before, TscnNodePaths.Root, after),
                _ => nextRoot,
            };

        // A node element and its subtree: its properties and connections take their comments from the parts of the
        // previous element that match them, reported at that element's place in the previous document, and its children
        // look up their own matches by unique id first, then by their path in the new document.
        private XscnNodeElement CarryNode(XscnNodeElement before, string path, XscnNodeElement after)
        {
            var carried = (XscnNodeElement)CarryComments(before, after);
            string reported = index.PathOf(before);
            return carried with
            {
                Properties = CarryProperties(before.Properties, reported, after.Properties),
                Connections = CarryConnections(before.Connections, reported, after.Connections),
                Children = [.. after.Children.Select(child => CarryChild(path, child))],
            };
        }

        private XscnNodeElement CarryChild(string parent, XscnNodeElement nextChild)
        {
            string path = ChildPath(parent, nextChild);
            return Match(path, nextChild) is { } before ? CarryNode(before, path, nextChild) : CarryBare(path, nextChild);
        }

        // A new element with no previous one: its own subtree still takes the comments of the elements that match it.
        private XscnNodeElement CarryBare(string path, XscnNodeElement after) =>
            after with
            {
                Children = [.. after.Children.Select(child => CarryChild(path, child))],
            };

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

        // The previous element a new one matches: by its unique id when the id occurs once in each document, else by its
        // path, and never an element already matched.
        private XscnNodeElement? Match(string path, XscnNodeElement after)
        {
            if (byId.TryGetValue(after, out XscnNodeElement? matched))
            {
                return matched;
            }

            if (index.Nodes.GetValueOrDefault(path) is not { } atPath)
            {
                return null;
            }

            return used.Add(atPath) ? atPath : null;
        }

        private XscnResource CarryResource(XscnResource before, string path, XscnResource after)
        {
            var carried = (XscnResource)CarryComments(before, after);
            XscnResource copy = carried with { Properties = CarryProperties(before.Properties, path, after.Properties) };
            copies[after] = copy;
            return copy;
        }

        // The x:Resources table: an entry takes comments only when the forward conversion kept its key from the previous
        // document's entries, so a key made from scratch that happens to equal a deleted entry's takes nothing.
        private ImmutableArray<XscnResource> CarryResources(ImmutableArray<XscnResource> after)
        {
            var matched = new HashSet<XscnResource>(ReferenceEqualityComparer.Instance);
            List<XscnResource> carried = [];
            foreach (XscnResource entry in after)
            {
                carried.Add(CarryEntry(entry, matched));
            }

            foreach (XscnResource before in previous.Resources)
            {
                if (!matched.Contains(before))
                {
                    ReportDroppedEntry(before, NoLongerInScene);
                }
            }

            return [.. carried];
        }

        private XscnResource CarryEntry(XscnResource entry, HashSet<XscnResource> matched)
        {
            if (entry.Key is not { } key || !keptKeys.Contains(key) || index.Resources.GetValueOrDefault(key) is not { } before)
            {
                return entry;
            }

            matched.Add(before);
            return CarryResource(before, key, entry);
        }

        // A part with no match loses its comments: what they described is gone.
        private static XscnElement CarryComments(XscnElement before, XscnElement after) =>
            after with
            {
                LeadingComments = before.LeadingComments,
                TrailingComments = before.TrailingComments,
            };

        // A matched pair of properties: any matched property takes the leading comments, but only one whose value still
        // holds an inline resource of the same class takes the trailing ones and the comments of that resource, since a
        // property's trailing comments belong to the resource it holds and a value of another shape cannot keep them.
        private XscnProperty CarryValue(XscnProperty before, string path, XscnProperty after)
        {
            XscnProperty matched = after with { LeadingComments = before.LeadingComments };
            if (before.Value is not XscnInline earlier)
            {
                return matched;
            }

            if (after.Value is XscnInline inline && SameClass(earlier.Resource.Type, inline.Resource.Type))
            {
                return matched with
                {
                    TrailingComments = before.TrailingComments,
                    Value = new XscnInline(CarryResource(earlier.Resource, path, inline.Resource)),
                };
            }

            ReportDroppedValue(before, earlier.Resource, path, after.Value);
            return matched;
        }

        // A property whose inline resource the new document does not hold: the property's trailing comments and the
        // resource's own comments drop together, and each property inside that resource keeps its own warning, named by
        // the property whose value held it.
        private void ReportDroppedValue(XscnProperty before, XscnResource resource, string path, XscnValue after)
        {
            string why = after is XscnInline inline ? $"its inline resource is now a {TypeName(inline.Resource.Type)}" : NoLongerInline;
            Report(
                $"property {before.GodotName} of {path}",
                path,
                [.. before.TrailingComments, .. resource.LeadingComments, .. resource.TrailingComments],
                why
            );
            ReportDroppedInnerProperties(resource, path, $"{before.GodotName} of {path}", why);
        }

        private static string TypeName(XscnType type) =>
            type switch
            {
                XscnClassType known => known.Class.Name,
                XscnUnknownType unknown => unknown.Name,
                XscnSceneType scene => scene.Path,
                _ => string.Empty,
            };

        private static bool SameClass(XscnType previous, XscnType next) =>
            (previous, next) switch
            {
                (XscnClassType left, XscnClassType right) => string.Equals(left.Class.Name, right.Class.Name, StringComparison.Ordinal),
                (XscnUnknownType left, XscnUnknownType right) => string.Equals(left.Name, right.Name, StringComparison.Ordinal),
                _ => false,
            };

        // Within a matched element, a property matched by its Godot name, ordinal among the properties that share one.
        private ImmutableArray<XscnProperty> CarryProperties(ImmutableArray<XscnProperty> before, string path, ImmutableArray<XscnProperty> after)
        {
            Dictionary<string, Queue<XscnProperty>> pending = Queued(before, property => property.GodotName);
            ImmutableArray<XscnProperty> carried =
            [
                .. after.Select(property => Take(pending, property.GodotName) is { } source ? CarryValue(source, path, property) : property),
            ];
            foreach (XscnProperty source in pending.Values.SelectMany(queue => queue))
            {
                ReportDroppedProperty(source, path, path, NoLongerInScene);
            }

            return carried;
        }

        // Within a matched element, by signal, target and method, several with the same triple in their order.
        private ImmutableArray<XscnConnection> CarryConnections(
            ImmutableArray<XscnConnection> before,
            string path,
            ImmutableArray<XscnConnection> after
        )
        {
            Dictionary<(string, string, string), Queue<XscnConnection>> pending = Queued(
                before,
                connection => (connection.Signal, connection.To, connection.Method)
            );
            ImmutableArray<XscnConnection> carried =
            [
                .. after.Select(connection =>
                    Take(pending, (connection.Signal, connection.To, connection.Method)) is { } source
                        ? connection with
                        {
                            LeadingComments = source.LeadingComments,
                        }
                        : connection
                ),
            ];
            foreach (XscnConnection source in pending.Values.SelectMany(queue => queue))
            {
                Report($"connection {source.Signal} -> {source.Method} of {path}", path, source.LeadingComments, NoLongerInScene);
            }

            return carried;
        }

        // The x:Uids entries, by path, ordinal among those with the same path.
        private ImmutableArray<XscnUid> CarryUids(ImmutableArray<XscnUid> before, ImmutableArray<XscnUid> after)
        {
            Dictionary<string, Queue<XscnUid>> pending = Queued(before, uid => uid.Path);
            ImmutableArray<XscnUid> carried =
            [
                .. after.Select(uid => Take(pending, uid.Path) is { } source ? uid with { LeadingComments = source.LeadingComments } : uid),
            ];
            foreach (XscnUid source in pending.Values.SelectMany(queue => queue))
            {
                Report($"the x:Uids entry for {source.Path}", source.Path, source.LeadingComments, NoLongerInScene);
            }

            return carried;
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

        // Every previous element no new one matched loses its comments and everything they name. The walk runs once the
        // whole carry is done, so an element a later branch of the new document claims is never reported.
        private void ReportUnmatched(string why)
        {
            if (previous.Root is XscnNodeElement root)
            {
                ReportUnmatchedNode(root, TscnNodePaths.Root, why);
            }
        }

        // An unmatched element is reported at its path in the previous document, and the walk goes on through a matched
        // one, whose own parts the carry already reported.
        private void ReportUnmatchedNode(XscnNodeElement element, string path, string why)
        {
            if (!used.Contains(element))
            {
                ReportDroppedElement(element, path, why);
            }

            foreach (XscnNodeElement child in element.Children)
            {
                ReportUnmatchedNode(child, ChildPath(path, child), why);
            }
        }

        private void ReportDroppedElement(XscnNodeElement element, string path, string why)
        {
            Report($"node {path}", path, ElementComments(element), why);
            foreach (XscnProperty property in element.Properties)
            {
                ReportDroppedProperty(property, path, path, why);
            }

            foreach (XscnConnection connection in element.Connections)
            {
                Report($"connection {connection.Signal} -> {connection.Method} of {path}", path, connection.LeadingComments, why);
            }
        }

        // A dropped property, and the inline resource it held: the property's own comments are one part, and each
        // commented property inside that resource is named by the property whose value held it.
        private void ReportDroppedProperty(XscnProperty property, string location, string owner, string why)
        {
            XscnResource? inline = (property.Value as XscnInline)?.Resource;
            Report($"property {property.GodotName} of {owner}", location, PropertyComments(property, inline), why);
            if (inline is not null)
            {
                ReportDroppedInnerProperties(inline, location, $"{property.GodotName} of {owner}", why);
            }
        }

        private void ReportDroppedInnerProperties(XscnResource resource, string location, string owner, string why)
        {
            foreach (XscnProperty property in resource.Properties)
            {
                ReportDroppedProperty(property, location, owner, why);
            }
        }

        private void ReportDroppedEntry(XscnResource entry, string why)
        {
            string location = entry.Key ?? string.Empty;
            Report($"the x:Resources entry {entry.Key}", location, ElementComments(entry), why);
            ReportDroppedInnerProperties(entry, location, location, why);
        }

        // A document of the other kind takes none of the comments of the one it replaces: every slot drops.
        private void ReportReplaced()
        {
            string why = previous.Kind == XscnDocumentKind.Scene ? "the file being replaced is a scene" : "the file being replaced is a resource";
            ReportUnmatched(why);
            if (previous.Root is XscnResource resource)
            {
                Report("the resource root", TscnNodePaths.Root, ElementComments(resource), why);
                ReportDroppedInnerProperties(resource, TscnNodePaths.Root, TscnNodePaths.Root, why);
            }

            Report("the x:Uids table", "x:Uids", [.. previous.UidsLeadingComments, .. previous.UidsTrailingComments], why);
            foreach (XscnUid uid in previous.Uids)
            {
                Report($"the x:Uids entry for {uid.Path}", uid.Path, uid.LeadingComments, why);
            }

            Report("the x:Resources table", "x:Resources", [.. previous.ResourcesLeadingComments, .. previous.ResourcesTrailingComments], why);
            foreach (XscnResource entry in previous.Resources)
            {
                ReportDroppedEntry(entry, why);
            }
        }

        // One warning per dropped part, quoting the first line of its first comment; a part whose slots are all empty
        // reports nothing.
        private void Report(string what, string location, IEnumerable<string> comments, string why)
        {
            if (FirstLine(comments) is not { } text)
            {
                return;
            }

            string message = $"a comment on {what} is dropped because {why}: \"{Quoted(text)}\"";
            issues.Add(new ConversionIssue(ConversionCodes.CommentDropped, ConversionSeverity.Warning, message, location));
        }

        private static string? FirstLine(IEnumerable<string> comments)
        {
            foreach (string comment in comments)
            {
                int end = comment.IndexOf('\n', StringComparison.Ordinal);
                string line = (end < 0 ? comment : comment[..end]).Trim();
                if (line.Length > 0)
                {
                    return line;
                }
            }

            return null;
        }

        // What a warning quotes: the text cut to 60 characters at a character boundary with an ellipsis when longer, and
        // a quote in it escaped so the message reads as one quoted string.
        private static string Quoted(string text)
        {
            if (text.Length <= QuotedLength)
            {
                return text.Replace("\"", "\\\"", StringComparison.Ordinal);
            }

            int length = char.IsHighSurrogate(text[QuotedLength - 1]) && char.IsLowSurrogate(text[QuotedLength]) ? QuotedLength - 1 : QuotedLength;
            return text[..length].Replace("\"", "\\\"", StringComparison.Ordinal) + "…";
        }

        private static IEnumerable<string> ElementComments(XscnElement element) => [.. element.LeadingComments, .. element.TrailingComments];

        private static IEnumerable<string> PropertyComments(XscnProperty property, XscnResource? inline) =>
            inline is null
                ? [.. property.LeadingComments, .. property.TrailingComments]
                : [.. property.LeadingComments, .. property.TrailingComments, .. inline.LeadingComments, .. inline.TrailingComments];

        // The new elements that match a previous one by unique id: an id used once in each document, between two roots or
        // between two elements that are not, since the roots always match each other at the root's path.
        private Dictionary<XscnNodeElement, XscnNodeElement> UniqueIdMatches(
            IReadOnlyList<XscnNodeElement> previousNodes,
            IReadOnlyList<XscnNodeElement> nextNodes
        )
        {
            Dictionary<int, XscnNodeElement> earlier = UniqueIdElements(previousNodes);
            Dictionary<int, int> after = IdCounts(nextNodes);
            var matches = new Dictionary<XscnNodeElement, XscnNodeElement>(ReferenceEqualityComparer.Instance);
            foreach (XscnNodeElement element in nextNodes)
            {
                if (Pair(element, earlier, after) is { } match && SameRootness(element, match, previous.Root, next.Root))
                {
                    matches[element] = match;
                }
            }

            return matches;
        }

        // The previous document's elements by a unique id that occurs exactly once there.
        private static Dictionary<int, XscnNodeElement> UniqueIdElements(IReadOnlyList<XscnNodeElement> elements)
        {
            Dictionary<int, int> counts = IdCounts(elements);
            Dictionary<int, XscnNodeElement> found = [];
            foreach (XscnNodeElement element in elements)
            {
                if (element.UniqueId is { } id && counts[id] == 1)
                {
                    found.TryAdd(id, element);
                }
            }

            return found;
        }

        private static XscnNodeElement? Pair(XscnNodeElement element, Dictionary<int, XscnNodeElement> earlier, Dictionary<int, int> after) =>
            element.UniqueId is { } id && after.GetValueOrDefault(id) == 1 && earlier.GetValueOrDefault(id) is { } match ? match : null;

        private static bool SameRootness(XscnNodeElement element, XscnNodeElement match, XscnElement previousRoot, XscnElement nextRoot) =>
            ReferenceEquals(element, nextRoot) == ReferenceEquals(match, previousRoot);

        // How many node elements a document has under each unique id, so that an id there twice is never used to match.
        private static Dictionary<int, int> IdCounts(IReadOnlyList<XscnNodeElement> elements)
        {
            Dictionary<int, int> counts = [];
            foreach (XscnNodeElement element in elements)
            {
                if (element.UniqueId is { } id)
                {
                    counts[id] = counts.GetValueOrDefault(id) + 1;
                }
            }

            return counts;
        }

        private static IEnumerable<XscnNodeElement> NodeElements(XscnElement root)
        {
            if (root is not XscnNodeElement node)
            {
                yield break;
            }

            yield return node;
            foreach (XscnNodeElement child in node.Children.SelectMany(NodeElements))
            {
                yield return child;
            }
        }

        /// <summary>What a previous document offers a match: its node elements by path, its table entries by key.</summary>
        private sealed class Index
        {
            public Index(XscnDocument previous)
            {
                if (previous.Root is XscnNodeElement root)
                {
                    Collect(root, TscnNodePaths.Root, Nodes, Paths);
                }

                foreach (XscnResource entry in previous.Resources)
                {
                    if (entry.Key is { } key)
                    {
                        Resources.TryAdd(key, entry);
                    }
                }
            }

            /// <summary>Its node elements, by their path from the root.</summary>
            public Dictionary<string, XscnNodeElement> Nodes { get; } = new(StringComparer.Ordinal);

            /// <summary>Its node elements' paths, for reporting a part where the previous document had it.</summary>
            public Dictionary<XscnNodeElement, string> Paths { get; } = new(ReferenceEqualityComparer.Instance);

            /// <summary>Its <c>x:Resources</c> entries, by key.</summary>
            public Dictionary<string, XscnResource> Resources { get; } = new(StringComparer.Ordinal);

            public string PathOf(XscnNodeElement element) => Paths[element];

            private static void Collect(
                XscnNodeElement element,
                string path,
                Dictionary<string, XscnNodeElement> nodes,
                Dictionary<XscnNodeElement, string> paths
            )
            {
                // Every element gets its path, even one a repeated sibling name left without one of its own, since a
                // unique id still matches it; only the path lookup is first-wins, and the walk goes on regardless.
                nodes.TryAdd(path, element);
                paths.TryAdd(element, path);
                foreach (XscnNodeElement child in element.Children)
                {
                    Collect(child, ChildPath(path, child), nodes, paths);
                }
            }
        }
    }
}
