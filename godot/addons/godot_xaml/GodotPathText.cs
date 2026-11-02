using System.Linq;
using Godot;

namespace GodotXaml.Addon;

/// <summary>Godot's relative-path functions its C# <c>StringExtensions</c> do not bind, ported from <c>core/string/ustring.cpp</c>.</summary>
internal static class GodotPathText
{
    /// <summary>
    /// The file's path relative to a folder, as <c>String::path_to_file</c> gives it (<c>ustring.cpp:4839-4849</c>):
    /// <code>
    /// String src = replace_char('\\', '/');
    /// String dst = p_path.replace_char('\\', '/').get_base_dir();
    /// String rel = src.path_to(dst);
    /// if (rel == dst) { return p_path; } else { return rel + p_path.get_file(); }
    /// </code>
    /// </summary>
    /// <param name="folder">The folder the path is relative to.</param>
    /// <param name="path">The file's path.</param>
    /// <returns>The relative path (<c>./</c> for the same folder, <c>../</c> to climb), or the path when there is none.</returns>
    public static string PathToFile(string folder, string path)
    {
        string source = folder.Replace('\\', '/');
        string target = path.Replace('\\', '/').GetBaseDir();
        string relative = PathTo(source, target);
        return relative == target ? path : relative + path.GetFile();
    }

    /// <summary>
    /// The folder's path relative to another, as <c>String::path_to</c> gives it (<c>ustring.cpp:4851-4917</c>):
    /// both get a trailing slash; a shared <c>res://</c> or <c>user://</c> root becomes <c>/</c>, two absolute paths stay,
    /// and otherwise the first segments must match and are cut; then
    /// <code>
    /// int dirs_to_backtrack = (src_dirs.size() - 1) - common_parent;
    /// String dir = String("../").repeat(dirs_to_backtrack);
    /// for (int i = common_parent + 1; i &lt; dst_dirs.size(); i++) { dir += dst_dirs[i] + "/"; }
    /// if (dir.length() == 0) { dir = "./"; }
    /// </code>
    /// </summary>
    private static string PathTo(string from, string to)
    {
        string source = WithSlash(from.Replace('\\', '/'));
        string target = WithSlash(to.Replace('\\', '/'));
        if (!CutRoots(ref source, ref target))
        {
            return to;
        }

        string[] sourceDirs = Inner(source).Split('/');
        string[] targetDirs = Inner(target).Split('/');
        int commonParent = CommonCount(sourceDirs, targetDirs) - 1;
        string dir = string.Concat(Enumerable.Repeat("../", sourceDirs.Length - 1 - commonParent));
        dir += string.Concat(targetDirs.Skip(commonParent + 1).Select(name => name + "/"));
        return dir.Length == 0 ? "./" : dir;
    }

    private static bool CutRoots(ref string source, ref string target)
    {
        foreach (string root in (string[])["res://", "user://"])
        {
            if (source.StartsWith(root, System.StringComparison.Ordinal) && target.StartsWith(root, System.StringComparison.Ordinal))
            {
                source = source.Replace(root, "/", System.StringComparison.Ordinal);
                target = target.Replace(root, "/", System.StringComparison.Ordinal);
                return true;
            }
        }

        if (source.StartsWith('/') && target.StartsWith('/'))
        {
            return true;
        }

        string sourceBegin = source.Split('/')[0];
        string targetBegin = target.Split('/')[0];
        if (sourceBegin != targetBegin)
        {
            return false;
        }

        source = source[sourceBegin.Length..];
        target = target[targetBegin.Length..];
        return true;
    }

    private static int CommonCount(string[] source, string[] target)
    {
        int common = 0;
        while (common < source.Length && common < target.Length && source[common] == target[common])
        {
            common++;
        }

        return common;
    }

    private static string WithSlash(string path) => path.EndsWith('/') ? path : path + "/";

    // substr(1, length - 2): the text between the leading and the trailing slash; Godot's substr with a negative count
    // reads to the end, which is empty for "/".
    private static string Inner(string path) => path.Length >= 2 ? path[1..^1] : "";
}
