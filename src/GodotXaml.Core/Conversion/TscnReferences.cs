using System.Collections.Immutable;
using System.Globalization;
using System.Xml;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// What a .tscn or .tres refers to and how often: its sections by id, which ext_resources are used, how each
/// sub_resource is referenced, which go inline and which into <c>x:Resources</c>, and each entry's key.
/// </summary>
internal sealed class TscnReferences
{
    private readonly Dictionary<string, TscnExtResource> extById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TscnSubResource> subById = new(StringComparer.Ordinal);
    private readonly List<TscnExtResource> extResources = [];
    private readonly List<TscnSubResource> subResources = [];
    private readonly HashSet<string> usedExt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Use> uses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> edges = new(StringComparer.Ordinal);
    private readonly HashSet<string> onCycle = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> keys = new(StringComparer.Ordinal);
    private readonly List<ConversionIssue> issues;
    private readonly Func<string?, string, bool> canInline;

    private TscnReferences(List<ConversionIssue> issues, Func<string?, string, bool> canInline)
    {
        this.issues = issues;
        this.canInline = canInline;
    }

    /// <summary>The ext_resources something refers to, in file order.</summary>
    public IEnumerable<TscnExtResource> UsedExtResources => extResources.Where(ext => usedExt.Contains(ext.Id));

    /// <summary>The sub_resources that go into <c>x:Resources</c>, in file order.</summary>
    public IEnumerable<TscnSubResource> KeyedSubResources => subResources.Where(sub => !IsInline(sub.Id));

    /// <summary>
    /// Reads a document's references, adding an issue for each id that names no section, each section with a repeated
    /// id, each unused section and each sub_resource on a cycle.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="issues">Where issues go.</param>
    /// <param name="canInline">
    /// Whether a property element can hold a resource set on a property: given the owning section's class (null for a node
    /// with no type, an instance or an override) and the property's Godot name. A bare reference on a property that cannot
    /// is counted as one inside value text, so its sub_resource goes into <c>x:Resources</c>.
    /// </param>
    /// <param name="previous">
    /// The document the conversion replaces, whose <c>x:Resources</c> keys the sub_resources that match its entries keep;
    /// null converts from scratch.
    /// </param>
    /// <returns>The references.</returns>
    public static TscnReferences Analyze(
        TscnDocument document,
        List<ConversionIssue> issues,
        Func<string?, string, bool> canInline,
        XscnDocument? previous
    )
    {
        var references = new TscnReferences(issues, canInline);
        references.Index(document);
        references.Count(document);
        references.ReportUnused();
        references.FindCycles();
        references.ChooseKeys(previous);
        return references;
    }

    /// <summary>The ext_resource with an id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The section, or null when none has the id.</returns>
    public TscnExtResource? Ext(string id) => extById.GetValueOrDefault(id);

    /// <summary>The sub_resource with an id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The section, or null when none has the id.</returns>
    public TscnSubResource? Sub(string id) => subById.GetValueOrDefault(id);

    /// <summary>
    /// Whether a sub_resource is written inline: referenced exactly once, as a bare property value, and not on a cycle
    /// (DECISIONS.md, "Shared sub-resources live in an x:Resources table").
    /// </summary>
    /// <param name="id">The sub_resource's id.</param>
    /// <returns>Whether it is.</returns>
    public bool IsInline(string id) => uses.TryGetValue(id, out Use? use) && use.Count == 1 && !use.InText && !onCycle.Contains(id);

    /// <summary>The <c>x:Key</c> of a sub_resource that goes into <c>x:Resources</c>.</summary>
    /// <param name="id">The sub_resource's id.</param>
    /// <returns>The key.</returns>
    public string KeyOf(string id) => keys[id];

    private void Index(TscnDocument document)
    {
        foreach (TscnExtResource ext in document.ExtResources)
        {
            AddSection(extById, extResources, ext, ext.Id, $"[ext_resource id=\"{ext.Id}\"]");
        }

        foreach (TscnSubResource sub in document.SubResources)
        {
            if (AddSection(subById, subResources, sub, sub.Id, $"[sub_resource id=\"{sub.Id}\"]"))
            {
                uses[sub.Id] = new Use();
                edges[sub.Id] = [];
            }
        }
    }

    private bool AddSection<T>(Dictionary<string, T> byId, List<T> sections, T section, string id, string location)
    {
        if (byId.TryAdd(id, section))
        {
            sections.Add(section);
            return true;
        }

        Report(
            ConversionCodes.DuplicateId,
            ConversionSeverity.Error,
            $"An earlier section already has the id '{id}'; this one is not converted.",
            location
        );
        return false;
    }

    private void Count(TscnDocument document)
    {
        foreach (TscnSubResource sub in subResources)
        {
            CountProperties(sub.Properties, sub.Type, sub.Id, $"[sub_resource id=\"{sub.Id}\"]");
        }

        CountProperties(document.ResourceProperties, document.Header.Type, null, "[resource]");
        foreach (TscnNode node in document.Nodes)
        {
            string location = $"[node name=\"{node.Name}\"]";
            if (node.Instance is { } instance)
            {
                ValueReferences.Visit(instance, reference => Note(reference, null, false, location));
            }

            CountProperties(node.Properties, node.Type, null, location);
        }

        foreach (TscnConnection connection in document.Connections)
        {
            string location = $"[connection signal=\"{connection.Signal}\" from=\"{connection.From}\"]";
            foreach (GodotValue bind in connection.Binds)
            {
                ValueReferences.Visit(bind, reference => Note(reference, null, false, location));
            }
        }
    }

    private void CountProperties(ImmutableArray<TscnProperty> properties, string? ownerClass, string? from, string location)
    {
        foreach (TscnProperty property in properties)
        {
            bool bare = property.Value is GodotResourceRef && canInline(ownerClass, property.Name);
            ValueReferences.Visit(property.Value, reference => Note(reference, from, bare, location));
        }
    }

    // One reference: an ExtResource marks its section used, a SubResource counts a use of its section and, from inside
    // another sub_resource, an edge of the reference graph.
    private void Note(GodotValue reference, string? from, bool bare, string location)
    {
        if (reference is not GodotResourceRef { Id: var id } resourceRef)
        {
            return;
        }

        if (resourceRef.Kind == GodotRefKind.External)
        {
            NoteExt(id, location);
            return;
        }

        if (!uses.TryGetValue(id, out Use? use))
        {
            Report(ConversionCodes.UnknownSubResourceId, ConversionSeverity.Error, $"SubResource(\"{id}\") names no [sub_resource].", location);
            return;
        }

        use.Count++;
        use.InText |= !bare;
        if (from is not null)
        {
            edges[from].Add(id);
        }
    }

    private void NoteExt(string id, string location)
    {
        if (extById.ContainsKey(id))
        {
            usedExt.Add(id);
        }
        else
        {
            Report(ConversionCodes.UnknownExtResourceId, ConversionSeverity.Error, $"ExtResource(\"{id}\") names no [ext_resource].", location);
        }
    }

    private void ReportUnused()
    {
        foreach (TscnExtResource ext in extResources.Where(ext => !usedExt.Contains(ext.Id)))
        {
            string message = $"Nothing refers to the ext_resource '{ext.Path}'; it is dropped.";
            Report(ConversionCodes.UnusedExtResource, ConversionSeverity.Warning, message, $"[ext_resource id=\"{ext.Id}\"]");
        }

        foreach (TscnSubResource sub in subResources.Where(sub => uses[sub.Id].Count == 0))
        {
            string message = "Nothing refers to this sub_resource; it is kept as an x:Resources entry.";
            Report(ConversionCodes.UnusedSubResource, ConversionSeverity.Warning, message, $"[sub_resource id=\"{sub.Id}\"]");
        }
    }

    private void FindCycles()
    {
        foreach (TscnSubResource sub in subResources.Where(sub => Reaches(sub.Id, sub.Id)))
        {
            onCycle.Add(sub.Id);
            string message = "This sub_resource refers back to itself through others, which Godot cannot load; it is kept as an x:Resources entry.";
            Report(ConversionCodes.SubResourceCycle, ConversionSeverity.Error, message, $"[sub_resource id=\"{sub.Id}\"]");
        }
    }

    private bool Reaches(string from, string target)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        Stack<string> pending = new(edges[from]);
        while (pending.TryPop(out string? id))
        {
            if (id == target)
            {
                return true;
            }

            if (seen.Add(id))
            {
                edges[id].ForEach(pending.Push);
            }
        }

        return false;
    }

    // The previous document's key first, for a sub_resource that matches one of its entries. Then the owner's ruling: the
    // resource_name when it is an XML name no earlier entry took, else the type and a counter per type, skipping keys
    // already taken; names are given out first so a counter never takes a later entry's name.
    private void ChooseKeys(XscnDocument? previous)
    {
        List<TscnSubResource> keyed = [.. KeyedSubResources];
        HashSet<string> taken = new(StringComparer.Ordinal);
        foreach (TscnSubResource sub in keyed)
        {
            KeepPreviousKey(sub, previous, taken);
        }

        foreach (TscnSubResource sub in keyed.Where(sub => !keys.ContainsKey(sub.Id)))
        {
            if (ResourceName(sub) is { } name && IsXmlName(name) && taken.Add(name))
            {
                keys[sub.Id] = name;
            }
        }

        Dictionary<string, int> counters = new(StringComparer.Ordinal);
        foreach (TscnSubResource sub in keyed.Where(sub => !keys.ContainsKey(sub.Id)))
        {
            keys[sub.Id] = NextKey(sub.Type, counters, taken);
        }
    }

    // The key of the previous document's first matching entry that no earlier sub_resource took.
    private void KeepPreviousKey(TscnSubResource sub, XscnDocument? previous, HashSet<string> taken)
    {
        foreach (string key in PreviousKeys.Keys(sub, previous, id => Ext(id)?.Path))
        {
            if (taken.Add(key))
            {
                keys[sub.Id] = key;
                return;
            }
        }
    }

    internal static string? ResourceName(TscnSubResource sub) =>
        sub.Properties.LastOrDefault(property => property.Name == "resource_name")?.Value is GodotString { Value.Length: > 0 } name
            ? name.Value
            : null;

    // An XML name without a colon, which is also a {Key} the .xscn parser reads.
    private static bool IsXmlName(string name) => XmlConvert.IsStartNCNameChar(name[0]) && name.All(XmlConvert.IsNCNameChar);

    private static string NextKey(string type, Dictionary<string, int> counters, HashSet<string> taken)
    {
        int counter = counters.GetValueOrDefault(type);
        string key;
        do
        {
            counter++;
            key = type + counter.ToString(CultureInfo.InvariantCulture);
        } while (!taken.Add(key));

        counters[type] = counter;
        return key;
    }

    private void Report(string code, ConversionSeverity severity, string message, string location) =>
        issues.Add(new ConversionIssue(code, severity, message, location));

    private sealed class Use
    {
        public int Count { get; set; }

        public bool InText { get; set; }
    }
}
