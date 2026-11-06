using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tscn;

/// <summary>
/// Writes a <see cref="TscnDocument"/> as Godot 4.7.2's text saver lays it out
/// (scene/resources/resource_format_text.cpp:1759-2159, ResourceFormatSaverTextInstance::save), with Godot 4.8's
/// scene <c>[resource]</c> and nested scenes where the document holds them (4.8 resource_format_text.cpp:1721-1866,
/// 2025-2143). Sections are written in model order; attributes the model has no field for follow the known ones.
/// </summary>
public static class TscnWriter
{
    // resource_format_text.h:46-48.
    private const int FormatVersion = 4;
    private const int FormatVersionCompat = 3;

    /// <summary>Writes a document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The text, LF line ends, ending after the last section line.</returns>
    public static string Write(TscnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CheckKindFields(document);
        int format = FormatOf(document);
        var context = new Context(new StringBuilder(), new ValueWriteOptions(Compat: format == FormatVersionCompat));
        WriteHeader(context.Text, document, format);
        WriteExtResources(context.Text, document.ExtResources);
        WriteSubResources(context, document);
        if (document.Kind == TscnKind.Resource)
        {
            // :1920-1921: the main resource comes last, with no blank line after it.
            context.Text.Append("[resource]\n");
            WriteProperties(context, document.ResourceProperties);
        }
        else
        {
            WriteScene(context, document);
        }

        return context.Text.ToString();
    }

    // The layout has no place for the other kind's sections: a resource file ends at its [resource] (:1920-1921) and a
    // scene file's [resource] holds the PackedScene's own properties (4.8 :2114-2117). Only a PackedScene sub-resource
    // writes nodes of its own (4.8 :2122-2125).
    private static void CheckKindFields(TscnDocument document)
    {
        bool resource = document.Kind == TscnKind.Resource;
        (string Field, bool Present)[] misplaced = resource
            ?
            [
                ("Nodes", !document.Nodes.IsEmpty),
                ("Connections", !document.Connections.IsEmpty),
                ("Editables", !document.Editables.IsEmpty),
                ("SceneProperties", !document.SceneProperties.IsEmpty),
            ]
            : [("ResourceProperties", !document.ResourceProperties.IsEmpty)];
        foreach ((string field, bool present) in misplaced)
        {
            if (present)
            {
                throw new InvalidOperationException($"A {document.Kind} document cannot hold {field}");
            }
        }

        if (resource && document.HasSceneResourceSection)
        {
            throw new InvalidOperationException("A Resource document cannot hold HasSceneResourceSection");
        }

        foreach (TscnSubResource sub in document.SubResources)
        {
            CheckNestedScene(sub);
        }
    }

    // 4.8 :2073-2075, 2122-2125: a nested scene takes _bundled's place, and its connections and editables name its nodes.
    private static void CheckNestedScene(TscnSubResource sub)
    {
        if (sub.Scene is null)
        {
            return;
        }

        string? wrong =
            sub.Type != "PackedScene" ? $"A sub-resource of type {sub.Type} cannot hold a Scene"
            : sub.Properties.Any(property => property.Name == "_bundled") ? $"The sub-resource {sub.Id} cannot hold both a Scene and _bundled"
            : sub.Scene.Nodes.IsEmpty && !(sub.Scene.Connections.IsEmpty && sub.Scene.Editables.IsEmpty)
                ? $"The sub-resource {sub.Id}'s Scene has connections or editables but no node"
            : null;
        if (wrong is not null)
        {
            throw new InvalidOperationException(wrong);
        }
    }

    /// <summary>The format the saver gives a document: 4 when it holds a value compat mode cannot write, else 3.</summary>
    /// <param name="document">The document.</param>
    /// <returns>3 or 4.</returns>
    public static int FormatOf(TscnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return TscnValues.NeedsFormat4(AllValues(document)) ? FormatVersion : FormatVersionCompat;
    }

    /// <summary>
    /// A property name as String::property_name_encode writes it (core/string/ustring.cpp:5053-5064): quoted with
    /// c_escape_multiline when it holds '=', '"', ';', '[', ']', a character below 33 or one above 126.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The encoded name.</returns>
    public static string EncodePropertyName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        bool quote = name.Any(c => c is '=' or '"' or ';' or '[' or ']' or < '!' or > '~');
        return quote ? "\"" + GodotStringEscape.CEscapeMultiline(name) + "\"" : name;
    }

    private static IEnumerable<GodotValue> AllValues(TscnDocument document) =>
        document
            .SubResources.SelectMany(sub => sub.Properties)
            .Concat(document.ResourceProperties)
            .Concat(document.SceneProperties)
            .Select(property => property.Value)
            .Concat(document.SubResources.Select(sub => sub.Scene).OfType<TscnNestedScene>().SelectMany(SceneValues))
            .Concat(SceneValues(new TscnNestedScene { Nodes = document.Nodes, Connections = document.Connections }));

    private static IEnumerable<GodotValue> SceneValues(TscnNestedScene scene) =>
        scene
            .Nodes.SelectMany(node => node.Properties)
            .Select(property => property.Value)
            .Concat(scene.Nodes.Select(node => node.Instance).OfType<GodotValue>())
            .Concat(scene.Connections.SelectMany(connection => connection.Binds));

    // :1800-1820.
    private static void WriteHeader(StringBuilder text, TscnDocument document, int format)
    {
        bool resource = document.Kind == TscnKind.Resource;
        text.Append(resource ? "[gd_resource " : "[gd_scene ");
        if (resource)
        {
            text.Append(Quoted("type", document.Header.Type ?? "Resource")).Append(' ');
            text.Append(Optional(document.Header.ScriptClass, value => Quoted("script_class", value) + " "));
        }

        text.Append("format=").Append(format.ToString(CultureInfo.InvariantCulture));
        text.Append(Optional(document.Header.Uid, value => " " + Quoted("uid", value)));
        text.Append(Extra(document.Header.Extra)).Append("]\n\n");
    }

    // :1879-1894.
    private static void WriteExtResources(StringBuilder text, ImmutableArray<TscnExtResource> extResources)
    {
        foreach (TscnExtResource ext in extResources)
        {
            text.Append("[ext_resource ").Append(Quoted("type", ext.Type));
            text.Append(Optional(ext.Uid, value => " " + Quoted("uid", value)));
            text.Append(' ').Append(Quoted("path", ext.Path)).Append(' ').Append(Quoted("id", ext.Id));
            text.Append(Extra(ext.Extra)).Append("]\n");
        }

        if (!extResources.IsEmpty)
        {
            text.Append('\n');
        }
    }

    // :1923-2004: each sub-resource is followed by a blank line, since the main resource or scene always follows. In
    // 4.8 a PackedScene's own nodes follow its property lines directly (:2122-2125).
    private static void WriteSubResources(Context context, TscnDocument document)
    {
        foreach (TscnSubResource sub in document.SubResources)
        {
            context.Text.Append("[sub_resource ").Append(Quoted("type", sub.Type)).Append(' ').Append(Quoted("id", sub.Id));
            context.Text.Append(Extra(sub.Extra)).Append("]\n");
            WriteProperties(context, sub.Properties);
            if (sub.Scene is not null)
            {
                WriteNodes(context, sub.Scene.Nodes, sub.Scene.Connections, sub.Scene.Editables);
            }

            context.Text.Append('\n');
        }
    }

    // 4.8 :2114-2117, 2127-2130, 2137-2139: the scene's [resource] is written when it has a property or the document
    // says it has the section (4.8 writes it for any PackedScene sub-resource), then a blank line, then the main scene.
    // 4.7.2's layout (:2010-2151) is the case with neither.
    private static void WriteScene(Context context, TscnDocument document)
    {
        if (!document.SceneProperties.IsEmpty || document.HasSceneResourceSection)
        {
            context.Text.Append("[resource]\n");
            WriteProperties(context, document.SceneProperties);
            context.Text.Append('\n');
        }

        WriteNodes(context, document.Nodes, document.Connections, document.Editables);
    }

    // 4.8 :1721-1866 (4.7.2 :2010-2151): the nodes with a blank line between each, then the connections and the
    // editables, each block after a blank line.
    private static void WriteNodes(
        Context context,
        ImmutableArray<TscnNode> nodes,
        ImmutableArray<TscnConnection> connections,
        ImmutableArray<TscnEditable> editables
    )
    {
        for (int i = 0; i < nodes.Length; i++)
        {
            TscnNode node = nodes[i];
            context.Text.Append("[node").Append(TscnNodeHeader.Write(node, context.Options)).Append(Extra(node.Extra)).Append("]\n");
            WriteProperties(context, node.Properties);
            if (i < nodes.Length - 1)
            {
                context.Text.Append('\n');
            }
        }

        WriteBlock(context.Text, connections.Select(connection => TscnNodeHeader.Connection(connection, context.Options)));
        WriteBlock(context.Text, editables.Select(editable => "[editable " + Escaped("path", editable.Path) + Extra(editable.Extra) + "]"));
    }

    // :2100-2103, 2146-2149: one blank line before the first line of a block, none between.
    private static void WriteBlock(StringBuilder text, IEnumerable<string> lines)
    {
        bool first = true;
        foreach (string line in lines)
        {
            text.Append(first ? "\n" : "").Append(line).Append('\n');
            first = false;
        }
    }

    // :1998, 2092.
    private static void WriteProperties(Context context, ImmutableArray<TscnProperty> properties)
    {
        foreach (TscnProperty property in properties)
        {
            context.Text.Append(EncodePropertyName(property.Name)).Append(" = ");
            context.Text.Append(GodotValueWriter.Write(property.Value, context.Options)).Append('\n');
        }
    }

    /// <summary><c>name="value"</c> with the value as it is, as the saver concatenates header strings.</summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    internal static string Quoted(string name, string value) => name + "=\"" + value + "\"";

    /// <summary><c>name="value"</c> with the value c-escaped, as the saver writes names and paths.</summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    internal static string Escaped(string name, string value) => name + "=\"" + GodotStringEscape.CEscape(value) + "\"";

    /// <summary>The text for a value that may be absent, or nothing.</summary>
    /// <param name="value">The value, or null.</param>
    /// <param name="format">How a present value is written.</param>
    /// <returns>The text.</returns>
    internal static string Optional(string? value, Func<string, string> format) => value is null ? "" : format(value);

    /// <summary>The attributes the model has no field for, each as <c> name=text</c>.</summary>
    /// <param name="extra">The attributes.</param>
    /// <returns>The text.</returns>
    internal static string Extra(ImmutableArray<TscnField> extra) => string.Concat(extra.Select(field => " " + field.Name + "=" + field.Text));

    private sealed record Context(StringBuilder Text, ValueWriteOptions Options);
}
