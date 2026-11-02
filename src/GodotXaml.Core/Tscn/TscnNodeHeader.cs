using System.Collections.Immutable;
using System.Globalization;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tscn;

/// <summary>The attributes of a <c>[node]</c> and a <c>[connection]</c> header, in the saver's order (resource_format_text.cpp:2024-2142).</summary>
internal static class TscnNodeHeader
{
    /// <summary>The node header's attributes, each with a leading space.</summary>
    /// <param name="node">The node.</param>
    /// <param name="options">The value write options.</param>
    /// <returns>The text between <c>[node</c> and the attributes the model has no field for.</returns>
    public static string Write(TscnNode node, ValueWriteOptions options) => Identity(node) + Ownership(node) + References(node, options);

    /// <summary>A whole <c>[connection]</c> line, without its line end.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="options">The value write options.</param>
    /// <returns>The line.</returns>
    public static string Connection(TscnConnection connection, ValueWriteOptions options)
    {
        // :2105-2142: flags only when not CONNECT_PERSIST, unbinds only when positive, "binds= " with its space.
        string text = "[connection " + TscnWriter.Escaped("signal", connection.Signal);
        text += " " + TscnWriter.Escaped("from", connection.From) + " " + TscnWriter.Escaped("to", connection.To);
        text += " " + TscnWriter.Escaped("method", connection.Method);
        text += connection.Flags == TscnConnection.PersistFlags ? "" : " flags=" + Itos(connection.Flags);
        text += IdPath("from_uid_path", connection.FromUidPath) + IdPath("to_uid_path", connection.ToUidPath);
        text += connection.Unbinds > 0 ? " unbinds=" + Itos(connection.Unbinds) : "";
        text += connection.Binds.IsEmpty ? "" : " binds= " + GodotValueWriter.Write(new GodotArray(connection.Binds, null), options);
        return text + TscnWriter.Extra(connection.Extra) + "]";
    }

    // :2025-2034: name, type, then parent and its id path, which only a node with a parent has.
    private static string Identity(TscnNode node)
    {
        string text = " " + TscnWriter.Escaped("name", node.Name);
        text += TscnWriter.Optional(node.Type, type => " " + TscnWriter.Quoted("type", type));
        return text
            + TscnWriter.Optional(node.Parent, parent => " " + TscnWriter.Escaped("parent", parent) + IdPath("parent_id_path", node.ParentIdPath));
    }

    // :2036-2048: owner unless "." and its id path, index quoted, unique_id bare unless 0.
    private static string Ownership(TscnNode node)
    {
        bool hasOwner = node.Owner is not null && node.Owner != ".";
        string text = hasOwner ? " " + TscnWriter.Escaped("owner", node.Owner!) + IdPath("owner_uid_path", node.OwnerUidPath) : "";
        text += node.Index is >= 0 ? " index=\"" + Itos(node.Index.Value) + "\"" : "";
        return text + (node.UniqueId is null or 0 ? "" : " unique_id=" + Itos(node.UniqueId.Value));
    }

    // :2050-2084: node_paths, groups sorted, then instance_placeholder and instance through the value writer.
    private static string References(TscnNode node, ValueWriteOptions options)
    {
        string text = node.NodePaths.IsEmpty ? "" : " node_paths=" + GodotValueWriter.Write(new GodotPackedStrings(node.NodePaths), options);
        text += node.Groups.IsEmpty ? "" : " groups=[" + string.Join(", ", node.Groups.Order(GroupOrder).Select(Group)) + "]";
        text += TscnWriter.Optional(
            node.InstancePlaceholder,
            path => " instance_placeholder=" + GodotValueWriter.Write(new GodotString(path), options)
        );
        return text + (node.Instance is null ? "" : " instance=" + GodotValueWriter.Write(node.Instance, options));
    }

    // :2060 groups.sort_custom<StringName::AlphCompare> compares with str_compare, which is code point order
    // (core/string/string_name.h:136-143), not UTF-16 unit order.
    private static readonly IComparer<string> GroupOrder = Comparer<string>.Create(
        (left, right) => GodotKeyOrder.Instance.Compare(new GodotString(left), new GodotString(right))
    );

    private static string Group(string group) => "\"" + GodotStringEscape.CEscape(group) + "\"";

    // Variant(PackedInt32Array).get_construct_string(), written only when the path is not empty.
    private static string IdPath(string name, ImmutableArray<int> path) =>
        path.IsEmpty ? "" : " " + name + "=" + GodotValueWriter.Write(new GodotPackedInts(path), ValueWriteOptions.Default);

    private static string Itos(int value) => value.ToString(CultureInfo.InvariantCulture);
}
