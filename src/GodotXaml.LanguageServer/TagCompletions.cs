using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer;

/// <summary>
/// The names a tag being typed takes, by the element it is in, as the tree reader accepts them: in a node, the node classes,
/// <c>x:Instance</c>, <c>x:Override</c>, <c>x:Connect</c>, <c>x:Prop</c> and the node's property elements; in a resource,
/// <c>x:Prop</c> and its property elements, never a class; on the root, also
/// <c>x:Uids</c> and <c>x:Resources</c>; in a property element, the classes of the property's hint; in <c>x:Resources</c>,
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

        // The elements the tag is in are those open at its '<'.
        int lessThan = context.Replace.Start - 1;
        if (lessThan < 0 || scope.Text[lessThan] != '<')
        {
            return null;
        }

        List<XscnCursorContext> chain = scope.OpenElements(lessThan);
        return chain.Count == 0 ? Root() : InElement(chain);
    }

    private IEnumerable<CompletionCandidate> Root() =>
        scope.Kind == XscnDocumentKind.Scene ? scope.Classes(NodeClass).Concat(scope.Keywords(["x:Instance"])) : scope.Classes(ResourceClass);

    private IEnumerable<CompletionCandidate>? InElement(List<XscnCursorContext> chain)
    {
        int index = chain.Count - 1;
        XscnCursorContext parent = chain[index];
        bool isRoot = index == 0;
        return scope.ShapeOf(parent) switch
        {
            ElementShape.ClassElement when scope.IsResource(chain, index) => ResourceChildren(parent, isRoot),
            ElementShape.ClassElement => NodeChildren(parent, isRoot),
            ElementShape.PropertyElement => PropertyValue(parent),
            ElementShape.FormatElement => FormatChildren(parent, isRoot),
            ElementShape.SceneTag => NodeChildren(null, isRoot),
            _ => null,
        };
    }

    private IEnumerable<CompletionCandidate>? FormatChildren(XscnCursorContext parent, bool isRoot) =>
        parent.ElementName switch
        {
            "Resources" => scope.Classes(ResourceClass),
            "Uids" => scope.Keywords(["x:Uid"]),
            "Instance" or "Override" => NodeChildren(null, isRoot),
            _ => null,
        };

    // A node's children: the node classes, the x: elements a node takes and, for a node of known class, its property elements.
    private IEnumerable<CompletionCandidate> NodeChildren(XscnCursorContext? node, bool isRoot)
    {
        IEnumerable<CompletionCandidate> children = scope.Classes(NodeClass).Concat(FormatElements(NodeElements, isRoot));
        return node is null ? children : children.Concat(PropertyElements(node));
    }

    // A resource's children: x:Prop and, for a resource of known class, its property elements; a resource holds no class element.
    private IEnumerable<CompletionCandidate> ResourceChildren(XscnCursorContext resource, bool isRoot)
    {
        IEnumerable<CompletionCandidate> format = FormatElements(ResourceElements, isRoot);
        return format.Concat(PropertyElements(resource));
    }

    private IEnumerable<CompletionCandidate> FormatElements(string[] names, bool isRoot) => scope.Keywords(isRoot ? names.Concat(RootTables) : names);

    // Owner.Property for each stored property of the owner's class that holds a resource.
    private IEnumerable<CompletionCandidate> PropertyElements(XscnCursorContext owner)
    {
        if (owner.Class is not { } cls)
        {
            return [];
        }

        return scope
            .Metadata.PropertiesOf(cls)
            .Where(p => p.IsStored && !p.Name.Contains('/') && HoldsResource(p))
            .Select(p => new CompletionCandidate(
                $"{owner.ElementName}.{p.CSharpName}",
                CompletionItemKind.Property,
                scope.TypeName(p.Type, p.ClassName)
            ));
    }

    private bool HoldsResource(GodotPropertyInfo property) =>
        property.Type == "Object" && NameRules.HoldsResource(scope.Metadata, property.ClassName);

    // The classes a property element's property holds: those its hint allows, less those it excludes.
    private IEnumerable<CompletionCandidate>? PropertyValue(XscnCursorContext propertyElement)
    {
        string name = propertyElement.ElementName!;
        string propertyName = name[(name.IndexOf('.', StringComparison.Ordinal) + 1)..];
        if (propertyElement.Class is not { } owner || scope.Metadata.FindPropertyByCSharpName(owner, propertyName) is not { } property)
        {
            return null;
        }

        return property.Type == "Object" ? scope.HintClasses(property.ClassName) : null;
    }
}
