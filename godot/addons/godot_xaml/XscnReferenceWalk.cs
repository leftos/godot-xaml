using System;
using System.Collections.Immutable;
using System.Linq;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Addon;

/// <summary>
/// Rebuilds a document with each file property and each <c>Resource()</c> reference in its values passed through a
/// function: the properties of nodes, overrides and resources, inline resources, connection binds, array and dictionary
/// items, and the scripts of typed containers. The <c>x:Uids</c> table is left to the caller.
/// </summary>
/// <param name="reference">The replacement of a <c>Resource()</c> reference.</param>
/// <param name="file">The replacement of a file property.</param>
internal sealed class XscnReferenceWalk(Func<GodotPathRef, GodotValue> reference, Func<XscnFile, XscnValue> file)
{
    /// <summary>The document with its references replaced.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The rebuilt document.</returns>
    public XscnDocument Map(XscnDocument document) =>
        document with
        {
            Resources = [.. document.Resources.Select(Resource)],
            Root = document.Root switch
            {
                XscnNodeElement node => Node(node),
                XscnResource resource => Resource(resource),
                _ => document.Root,
            },
        };

    private XscnNodeElement Node(XscnNodeElement element) =>
        element with
        {
            Properties = Properties(element.Properties),
            Connections = [.. element.Connections.Select(connection => connection with { Binds = [.. connection.Binds.Select(Value)] })],
            Children = [.. element.Children.Select(Node)],
        };

    private XscnResource Resource(XscnResource resource) => resource with { Properties = Properties(resource.Properties) };

    private ImmutableArray<XscnProperty> Properties(ImmutableArray<XscnProperty> properties) =>
        [.. properties.Select(property => property with { Value = PropertyValue(property.Value) })];

    private XscnValue PropertyValue(XscnValue value) =>
        value switch
        {
            XscnScalar scalar => new XscnScalar(Value(scalar.Value)),
            XscnFile path => file(path),
            XscnInline inline => inline with { Resource = Resource(inline.Resource) },
            _ => value,
        };

    private GodotValue Value(GodotValue value) =>
        value switch
        {
            GodotPathRef path => reference(path),
            GodotArray array => new GodotArray([.. array.Items.Select(Value)], Type(array.ElementType)),
            GodotDictionary dictionary => new GodotDictionary(
                [.. dictionary.Entries.Select(entry => new GodotEntry(Value(entry.Key), Value(entry.Value)))],
                Type(dictionary.KeyType),
                Type(dictionary.ValueType)
            ),
            _ => value,
        };

    private GodotTypeRef? Type(GodotTypeRef? type) => type?.Script is { } script ? new GodotTypeRef(Value(script)) : type;
}
