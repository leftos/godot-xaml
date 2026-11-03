namespace GodotXaml.Core.Conversion;

/// <summary>
/// The conversion issue codes, one per kind of issue: <c>GXC00xx</c> on the way from .tscn/.tres, <c>GXC01xx</c> on
/// the way back. A code never changes meaning once released.
/// </summary>
public static class ConversionCodes
{
    /// <summary>A section attribute the model keeps unparsed (<c>Extra</c>); the .xscn has no place for it, so it is dropped.</summary>
    public const string UnrepresentableAttribute = "GXC0001";

    /// <summary>An <c>ExtResource("id")</c> naming no <c>[ext_resource]</c>; the reference is kept as it stands.</summary>
    public const string UnknownExtResourceId = "GXC0002";

    /// <summary>A <c>SubResource("id")</c> naming no <c>[sub_resource]</c>; the reference is kept as it stands.</summary>
    public const string UnknownSubResourceId = "GXC0003";

    /// <summary>An <c>[ext_resource]</c> nothing refers to, which Godot never writes; it is dropped.</summary>
    public const string UnusedExtResource = "GXC0004";

    /// <summary>A <c>[sub_resource]</c> nothing refers to, which Godot never writes; it is kept as an <c>x:Resources</c> entry.</summary>
    public const string UnusedSubResource = "GXC0005";

    /// <summary>A <c>[sub_resource]</c> that refers back to itself through others; it is kept as an <c>x:Resources</c> entry.</summary>
    public const string SubResourceCycle = "GXC0006";

    /// <summary>A second section with an id an earlier one has; the later section is not converted.</summary>
    public const string DuplicateId = "GXC0007";

    /// <summary>A class the metadata does not know (a GDExtension class); it is kept by its name.</summary>
    public const string UnknownClass = "GXC0008";

    /// <summary>
    /// A <c>[node]</c> whose <c>parent=</c> names no earlier node, a node after the first with no <c>parent=</c>, or a node
    /// with neither <c>type</c> nor <c>instance</c> outside any instance; it and its subtree are not converted.
    /// </summary>
    public const string NodeParentNotFound = "GXC0009";

    /// <summary>
    /// An <c>instance=</c> that is not <c>ExtResource("id")</c> of an existing section; the node and its subtree are not
    /// converted, and a <c>Node</c> stands in for a root.
    /// </summary>
    public const string InstanceNotAScene = "GXC0010";

    /// <summary>A second child of one parent with the same name; the later one is not converted.</summary>
    public const string RepeatedSiblingName = "GXC0011";

    /// <summary>A <c>[connection]</c> whose <c>from</c> names no node, inside or outside an instance; it is not converted.</summary>
    public const string ConnectionEmitterNotFound = "GXC0012";

    /// <summary>An <c>[editable]</c> whose <c>path</c> names no node; it is not converted.</summary>
    public const string EditableNotFound = "GXC0013";

    /// <summary>A scene with no <c>[node]</c>, or whose first one has neither a type nor a scene; a <c>Node</c> stands in for the root.</summary>
    public const string RootNotConvertible = "GXC0014";

    /// <summary>
    /// A node with both <c>instance=</c> and <c>instance_placeholder=</c>; Godot loads it as a placeholder, so
    /// <c>instance=</c> is dropped.
    /// </summary>
    public const string InstanceAndPlaceholder = "GXC0015";

    /// <summary>
    /// A file whose class neither the host, the table by extension nor the property using it gives; its
    /// <c>[ext_resource]</c> says <c>type="Resource"</c>.
    /// </summary>
    public const string UnknownExtResourceType = "GXC0101";

    /// <summary>A <c>{Key}</c> or <c>SubResource("Key")</c> naming no <c>x:Resources</c> entry; a bare one is dropped, one in text kept.</summary>
    public const string UnknownKey = "GXC0102";

    /// <summary>A value the parser could not read (<see cref="Xscn.XscnInvalid"/>); the property is dropped.</summary>
    public const string InvalidValue = "GXC0103";

    /// <summary>An <c>ExtResource(…)</c> inside .xscn value text, which names no section there; it is kept as it stands.</summary>
    public const string ExtResourceInText = "GXC0104";

    /// <summary>An <c>x:Resources</c> entry nothing refers to, which Godot would not save; it is dropped.</summary>
    public const string UnusedResourceEntry = "GXC0105";

    /// <summary>A resource with a script and no <see cref="ConversionOptions.ScriptClassOf"/>; <c>script_class=</c> is not written.</summary>
    public const string ScriptClassUnknown = "GXC0106";

    /// <summary>A resource whose tag names a scene, or a resource document whose root is a node; it is written as <c>Resource</c>.</summary>
    public const string NotAResource = "GXC0107";

    /// <summary>A resource that refers back to itself through others; the reference that closes the loop is not followed.</summary>
    public const string ResourceCycle = "GXC0108";

    /// <summary>An <c>x:Resources</c> entry without an <c>x:Key</c>, which nothing can refer to; it is dropped.</summary>
    public const string MissingKey = "GXC0109";

    /// <summary>An <c>x:Resources</c> entry whose key an earlier entry has; the later entry is dropped.</summary>
    public const string RepeatedKey = "GXC0110";

    /// <summary>A <c>Resource("uid://…")</c> without a path, which an <c>[ext_resource]</c> cannot be written for; it is kept as it stands.</summary>
    public const string UidWithoutPath = "GXC0111";

    /// <summary>A scene document whose root is a resource, not a node; no <c>[node]</c> is written.</summary>
    public const string SceneRootNotANode = "GXC0112";

    /// <summary>An <c>x:Override</c> that is not a direct child of an instance or the inherited root; its path is taken under its parent.</summary>
    public const string OverrideOutsideInstance = "GXC0113";

    /// <summary>
    /// An <c>x:Override</c> that has no <c>Path</c>, or a <c>Path</c> with an empty, <c>.</c> or <c>..</c> segment, which
    /// names no node; it and what it holds are not written.
    /// </summary>
    public const string OverrideWithoutPath = "GXC0114";

    /// <summary>A node named unique (<c>Name="%X"</c>) whose <c>unique_name_in_owner</c> is not true; it is written unique.</summary>
    public const string UniqueNameConflict = "GXC0115";

    /// <summary>A placeholder (<c>x:InstancePlaceholder</c>) whose tag names another class or a scene; the tag is dropped.</summary>
    public const string PlaceholderTypeDropped = "GXC0116";

    /// <summary>A comment of the previous document that the converted document cannot carry; it is dropped and warned about.</summary>
    public const string CommentDropped = "GXC0117";

    /// <summary>
    /// An instanced scene whose class <see cref="ConversionOptions.SceneFiles"/> could not give (not given, unparsable, a
    /// cycle, a node path it does not hold, a <c>uid://</c> reference); the section keeps its written property order.
    /// </summary>
    public const string InstancedClassUnreadable = "GXC0118";

    /// <summary>
    /// A property whose name holds a character XML cannot hold, so neither an attribute nor an <c>x:Prop</c> can name
    /// it; the property is dropped.
    /// </summary>
    public const string PropertyNameNotXml = "GXC0119";
}
