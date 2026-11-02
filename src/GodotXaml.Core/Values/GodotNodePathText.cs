namespace GodotXaml.Core.Values;

/// <summary>
/// A NodePath's text, parsed and printed as Godot does (core/string/node_path.cpp:394-472, 170-188): the names before
/// the first <c>:</c> split on <c>/</c> with the empty ones dropped, then the subnames after it split on <c>:</c>. The
/// text is not simplified, so <c>./x</c> and <c>../a/./b</c> keep their segments.
/// </summary>
internal static class GodotNodePathText
{
    /// <summary>The text Godot prints for the NodePath it parses from the given text.</summary>
    /// <param name="text">The path text.</param>
    /// <returns>Godot's printed form of the path: <see cref="string.Empty"/> for the empty NodePath.</returns>
    public static string Normalize(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        bool absolute = text[0] == '/';
        int subpathPosition = text.IndexOf(':');
        List<string> subnames = new();
        if (subpathPosition != -1 && !ReadSubnames(text, subpathPosition, subnames))
        {
            return string.Empty;
        }

        List<string> names = ReadNames(Truncated(text, subpathPosition), absolute);
        return IsEmpty(names, absolute, subnames) ? string.Empty : Print(names, absolute, subnames);
    }

    private static string Truncated(string text, int subpathPosition) => subpathPosition == -1 ? text : text[..subpathPosition];

    // node_path.cpp:441: no names, no leading '/', no subnames is the empty NodePath, which prints as "".
    private static bool IsEmpty(List<string> names, bool absolute, List<string> subnames) => names.Count == 0 && !absolute && subnames.Count == 0;

    // node_path.cpp:170-188: the names joined by '/', a leading '/' when absolute, then the subnames by ':'.
    private static string Print(List<string> names, bool absolute, List<string> subnames)
    {
        string printed = (absolute ? "/" : string.Empty) + string.Join('/', names);
        return subnames.Count == 0 ? printed : printed + ":" + string.Join(':', subnames);
    }

    // node_path.cpp:407-427: the subnames are the texts between the colons after the first, an empty one being an
    // error that leaves the whole NodePath empty, except at the end of the path.
    private static bool ReadSubnames(string text, int subpathPosition, List<string> subnames)
    {
        int from = subpathPosition + 1;
        for (int i = from; i <= text.Length; i++)
        {
            char c = i == text.Length ? '\0' : text[i];
            if (c != ':' && c != '\0')
            {
                continue;
            }

            if (i == from && c == '\0')
            {
                continue;
            }

            if (i == from)
            {
                return false;
            }

            subnames.Add(text[from..i]);
            from = i + 1;
        }

        return true;
    }

    // node_path.cpp:429-471: a name is a run of text between slashes, the runs of empty text dropped.
    private static List<string> ReadNames(string path, bool absolute)
    {
        List<string> names = new();
        int from = absolute ? 1 : 0;
        bool lastIsSlash = true;
        for (int i = from; i <= path.Length; i++)
        {
            char c = i == path.Length ? '\0' : path[i];
            if (c != '/' && c != '\0')
            {
                lastIsSlash = false;
                continue;
            }

            if (!lastIsSlash)
            {
                names.Add(path[from..i]);
            }

            from = i + 1;
            lastIsSlash = true;
        }

        return names;
    }
}
