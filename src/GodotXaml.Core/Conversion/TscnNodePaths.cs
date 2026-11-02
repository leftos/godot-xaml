namespace GodotXaml.Core.Conversion;

/// <summary>
/// A node's path relative to the scene root as a .tscn writes it: <c>.</c> for the root, the name for a child of the
/// root, names joined by <c>/</c> below that. A <c>[node]</c>'s <c>parent=</c> and a connection's <c>from</c> and
/// <c>to</c> are such paths.
/// </summary>
internal static class TscnNodePaths
{
    /// <summary>The root's path, and the <c>parent=</c> of the root's children.</summary>
    public const string Root = ".";

    /// <summary>The path of a node from its <c>parent=</c> and its name.</summary>
    /// <param name="parent">The node's <c>parent=</c>, or null for the root.</param>
    /// <param name="name">The node's name.</param>
    /// <returns>The node's path.</returns>
    public static string PathOf(string? parent, string name) =>
        parent switch
        {
            null => Root,
            Root => name,
            _ => parent + "/" + name,
        };

    /// <summary>The <c>parent=</c> of the node at a path below the root.</summary>
    /// <param name="path">The node's path; not the root's.</param>
    /// <returns>The parent's path.</returns>
    public static string ParentOf(string path)
    {
        if (path == Root)
        {
            throw new ArgumentException("The root has no parent.", nameof(path));
        }

        int slash = path.LastIndexOf('/');
        return slash < 0 ? Root : path[..slash];
    }
}
