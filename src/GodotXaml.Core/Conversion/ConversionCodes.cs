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
}
