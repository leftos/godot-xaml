namespace GodotXaml.Core.Diagnostics;

/// <summary>How serious a diagnostic is. Any error fails a Godot load; a warning does not.</summary>
public enum XscnSeverity
{
    /// <summary>The file is not valid; a load fails.</summary>
    Error,

    /// <summary>The file loads, but something in it is not what the tools know.</summary>
    Warning,
}

/// <summary>A problem found in a source text.</summary>
/// <param name="Code">The stable code, one of <see cref="XscnCodes"/>.</param>
/// <param name="Severity">Error or warning.</param>
/// <param name="Message">What is wrong and, where it is known, what was expected.</param>
/// <param name="Span">The text the problem lies in.</param>
public sealed record XscnDiagnostic(string Code, XscnSeverity Severity, string Message, TextSpan Span);

/// <summary>The diagnostic codes, one per kind of problem. A code never changes meaning once released.</summary>
public static class XscnCodes
{
    /// <summary>The text is not well-formed XML (an unclosed tag, a missing quote); the XML parser's own message.</summary>
    public const string XmlSyntax = "GX0001";

    /// <summary>A comment inside a start tag, which XML allows only between elements.</summary>
    public const string CommentInStartTag = "GX0002";

    /// <summary>Text where only elements and comments belong.</summary>
    public const string UnexpectedText = "GX0003";

    /// <summary>The document has no root element.</summary>
    public const string MissingRoot = "GX0004";

    /// <summary>An <c>x:</c> element the format does not define.</summary>
    public const string UnknownFormatElement = "GX0101";

    /// <summary>An <c>x:</c> attribute the format does not define, or one the element cannot carry.</summary>
    public const string UnknownFormatAttribute = "GX0102";

    /// <summary>A prefix no namespace declaration on the root binds.</summary>
    public const string UndeclaredPrefix = "GX0103";

    /// <summary>A node without a name: no <c>Name</c> attribute, an empty one, or <c>Name="%"</c>.</summary>
    public const string MissingName = "GX0104";

    /// <summary>A value that is not text of its property's type.</summary>
    public const string InvalidValue = "GX0105";

    /// <summary>An integer property's value that is neither one of its names nor an integer.</summary>
    public const string UnknownEnumMember = "GX0106";

    /// <summary>A tag naming a class the metadata does not know; the element is kept as a node of unknown type.</summary>
    public const string UnknownClass = "GX0107";

    /// <summary>An attribute an element requires is missing (<c>Path</c> on <c>x:Override</c>, <c>x:Key</c> in <c>x:Resources</c>).</summary>
    public const string MissingAttribute = "GX0108";

    /// <summary>A namespace declaration that is not on the root element.</summary>
    public const string MisplacedNamespace = "GX0109";

    /// <summary>
    /// A namespace declaration the format does not accept: a default <c>xmlns</c>, <c>x</c> bound to another URI, a
    /// second prefix bound to the format namespace, or a scene prefix bound to something other than a <c>res://</c> folder.
    /// </summary>
    public const string InvalidNamespace = "GX0110";

    /// <summary>An element where the format does not allow it (<c>x:Uids</c> below the root, a node inside a resource).</summary>
    public const string MisplacedElement = "GX0111";

    /// <summary>A property element whose owner is not its parent's tag, or that does not hold exactly one resource.</summary>
    public const string InvalidPropertyElement = "GX0112";

    /// <summary>An attribute an element already has, which XML forbids; the first value is kept and the later ones dropped.</summary>
    public const string DuplicateAttribute = "GX0113";

    /// <summary>An attribute whose prefix is a scene namespace, which names elements, not attributes.</summary>
    public const string ScenePrefixedAttribute = "GX0114";

    /// <summary>A second <c>&lt;x:Uids&gt;</c> or <c>&lt;x:Resources&gt;</c> on the root; it is not read.</summary>
    public const string SecondTable = "GX0115";

    /// <summary>An <c>x:Key</c> an earlier entry of <c>&lt;x:Resources&gt;</c> already has; the later resource is not read.</summary>
    public const string RepeatedKey = "GX0116";

    /// <summary>
    /// A class tag that is not a Resource where a resource is expected (inside a property element, in x:Resources, the root
    /// of an .xres).
    /// </summary>
    public const string NotAResource = "GX0117";

    /// <summary>An <c>x:Connect</c> with both <c>Signal</c> and <c>GodotSignal</c>; the connection is not read.</summary>
    public const string SignalAndGodotSignal = "GX0118";

    /// <summary>A scene's root is an instance placeholder, which Godot refuses to load.</summary>
    public const string PlaceholderRoot = "GX0119";

    /// <summary>An element nested deeper than the reader accepts; it and everything inside it are not read.</summary>
    public const string NestingTooDeep = "GX0120";

    /// <summary>The root's <c>x:Godot</c> is not a Godot version as <c>&lt;major&gt;.&lt;minor&gt;</c>; it is not kept.</summary>
    public const string MalformedGodotMarker = "GX0121";
}
