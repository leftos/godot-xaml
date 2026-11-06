using System.Collections.Immutable;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer;

/// <summary>
/// The names a tag being typed takes, by the element it is in, as the tree reader accepts them: in a node, the node classes,
/// <c>x:Instance</c>, <c>x:Override</c>, <c>x:Connect</c>, <c>x:Prop</c> and the node's property elements; in a resource,
/// <c>x:Prop</c> and its property elements, never a class; on the root, also
/// <c>x:Uids</c> and <c>x:Resources</c>, and on a scene's root with Godot 4.8 or later <c>x:Scene</c> when it has none; in
/// <c>x:Scene</c>, what a resource takes; in a <c>PackedScene</c> with Godot 4.8 or later, also the node classes and
/// <c>x:Instance</c>, its scene's root; in a property element, the classes of the property's hint; in <c>x:Resources</c>,
/// the resource classes; in <c>x:Uids</c>, <c>x:Uid</c>. A scene's root is a node class or <c>x:Instance</c>, a resource
/// file's a resource class. A scene tag's or an unknown prefix gets none.
/// </summary>
/// <param name="scope">The document.</param>
/// <param name="context">The tag name's context.</param>
internal sealed class TagCompletions(CompletionScope scope, XscnCursorContext context)
{
    private const string NodeClass = "Node";
    private const string ResourceClass = "Resource";

    private static readonly string[] NodeElements = ["x:Instance", "x:Override", "x:Connect", "x:Prop"];
    private static readonly string[] ResourceElements = ["x:Prop"];
    private static readonly string[] RootTables = ["x:Uids", "x:Resources"];

    /// <summary>The candidates.</summary>
    /// <returns>The candidates, or null where the tag takes none.</returns>
    public IEnumerable<CompletionCandidate>? Candidates()
    {
        if (context.ElementPrefix is { } prefix && prefix != scope.FormatPrefix)
        {
            return null;
        }

        int lessThan = context.Replace.Start - 1;
        if (lessThan < 0 || scope.Text[lessThan] != '<')
        {
            return null;
        }

        // The elements the tag is in are the context's ancestors.
        ImmutableArray<XscnOpenElement> chain = context.Ancestors;
        return chain.Length == 0 ? Root() : InElement(chain);
    }

    private IEnumerable<CompletionCandidate> Root() =>
        scope.Kind == XscnDocumentKind.Scene ? scope.Classes(NodeClass).Concat(scope.Keywords(["x:Instance"])) : scope.Classes(ResourceClass);

    private IEnumerable<CompletionCandidate>? InElement(ImmutableArray<XscnOpenElement> chain)
    {
        int index = chain.Length - 1;
        XscnOpenElement parent = chain[index];
        bool isRoot = index == 0;
        return scope.ShapeOf(parent) switch
        {
            ElementShape.ClassElement when scope.IsResource(chain, index) && scope.HoldsScene(parent) && scope.ReadsScenes && !HasOtherSceneRoot() =>
                ResourceChildren(parent, isRoot).Concat(scope.Classes(NodeClass)).Concat(scope.Keywords(["x:Instance"])),
            ElementShape.ClassElement when scope.IsResource(chain, index) => ResourceChildren(parent, isRoot),
            ElementShape.ClassElement => NodeChildren(parent, isRoot),
            ElementShape.PropertyElement => PropertyValue(parent),
            ElementShape.FormatElement => FormatChildren(parent, isRoot),
            ElementShape.SceneTag => NodeChildren(null, isRoot),
            _ => null,
        };
    }

    private IEnumerable<CompletionCandidate>? FormatChildren(XscnOpenElement parent, bool isRoot) =>
        parent.LocalName switch
        {
            "Resources" => scope.Classes(ResourceClass),
            "Uids" => scope.Keywords(["x:Uid"]),
            "Instance" or "Override" => NodeChildren(null, isRoot),
            "Scene" => ResourceChildren(parent, false),
            _ => null,
        };

    // A node's children: the node classes, the x: elements a node takes and, for a node of known class, its property elements.
    private IEnumerable<CompletionCandidate> NodeChildren(XscnOpenElement? node, bool isRoot)
    {
        IEnumerable<CompletionCandidate> children = scope.Classes(NodeClass).Concat(FormatElements(NodeElements, isRoot));
        return node is null ? children : children.Concat(PropertyElements(node));
    }

    // A resource's children: x:Prop and, for a resource of known class, its property elements; a resource holds no class element.
    private IEnumerable<CompletionCandidate> ResourceChildren(XscnOpenElement resource, bool isRoot)
    {
        IEnumerable<CompletionCandidate> format = FormatElements(ResourceElements, isRoot);
        return format.Concat(PropertyElements(resource));
    }

    private IEnumerable<CompletionCandidate> FormatElements(string[] names, bool isRoot) =>
        scope.Keywords(isRoot ? names.Concat(RootTables).Concat(SceneElement()) : names);

    // x:Scene on a scene's root, when its Godot reads it (4.8 or later) and the root has none but the one being typed.
    private IEnumerable<string> SceneElement()
    {
        if (scope.Kind != XscnDocumentKind.Scene || !scope.ReadsScenes)
        {
            return [];
        }

        return scope.Parse.Document.Scene is { Span: var span } && !AtCursor(span) ? [] : ["x:Scene"];
    }

    // Whether the innermost PackedScene of the parse around the cursor holds a scene root other than the one being typed.
    private bool HasOtherSceneRoot()
    {
        XscnResource? packed = Resources(scope.Parse.Document).Where(r => r.SceneRoot is not null && AtCursor(r.Span)).MinBy(r => r.Span.Length);
        return packed?.SceneRoot is { Span: var span } && !AtCursor(span);
    }

    private bool AtCursor(TextSpan span) => span.Start <= context.Replace.Start && context.Replace.Start <= span.End;

    // Every resource of a document, at any depth: the tables, x:Scene, inline resources and those inside scene roots.
    private static IEnumerable<XscnResource> Resources(XscnDocument document)
    {
        List<XscnResource> top = [.. document.Resources];
        if (document.Scene is { } scene)
        {
            top.Add(scene);
        }

        if (document.Root is XscnResource root)
        {
            top.Add(root);
        }

        IEnumerable<XscnResource> inNodes = document.Root is XscnNodeElement node ? InNode(node) : [];
        return top.SelectMany(WithInner).Concat(inNodes);
    }

    private static IEnumerable<XscnResource> WithInner(XscnResource resource) =>
        [resource, .. Inline(resource.Properties), .. resource.SceneRoot is { } root ? InNode(root) : []];

    private static IEnumerable<XscnResource> InNode(XscnNodeElement node) => [.. Inline(node.Properties), .. node.Children.SelectMany(InNode)];

    private static IEnumerable<XscnResource> Inline(ImmutableArray<XscnProperty> properties) =>
        properties.Select(p => p.Value).OfType<XscnInline>().SelectMany(i => WithInner(i.Resource));

    // Owner.Property for each stored property of the owner's class that holds a resource.
    private IEnumerable<CompletionCandidate> PropertyElements(XscnOpenElement owner)
    {
        if (owner.Class is not { } cls)
        {
            return [];
        }

        // x:Scene's property elements keep the format's prefix: <x:Scene.Property>.
        string tag = owner.Prefix is { } prefix ? $"{prefix}:{owner.LocalName}" : owner.LocalName;
        return scope
            .Metadata.PropertiesOf(cls)
            .Where(p => p.IsStored && !p.Name.Contains('/') && HoldsResource(p))
            .Select(p => Named($"{tag}.{p.CSharpName}", scope.TypeName(p.Type, p.ClassName)));
    }

    // A property element's name, its label and its insert alike.
    private static CompletionCandidate Named(string name, string detail) => new(name, CompletionItemKind.Property, detail, name);

    private bool HoldsResource(GodotPropertyInfo property) =>
        property.Type == "Object" && NameRules.HoldsResource(scope.Metadata, property.ClassName);

    // The classes a property element's property holds: those its hint allows, less those it excludes.
    private IEnumerable<CompletionCandidate>? PropertyValue(XscnOpenElement propertyElement)
    {
        string name = propertyElement.LocalName;
        string propertyName = name[(name.IndexOf('.', StringComparison.Ordinal) + 1)..];
        if (propertyElement.Class is not { } owner || scope.Metadata.FindPropertyByCSharpName(owner, propertyName) is not { } property)
        {
            return null;
        }

        return property.Type == "Object" ? scope.HintClasses(property.ClassName) : null;
    }
}
