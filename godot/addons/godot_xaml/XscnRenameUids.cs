using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Addon;

/// <summary>
/// Gives each path a rename produced the uid of the file it now names, as Godot's text loader does when it writes an
/// <c>ext_resource</c> back after a rename (<c>resource_format_text.cpp:1076-1080</c>):
/// <code>
/// ResourceUID::ID uid = ResourceSaver::get_resource_id_for_path(path);
/// if (uid != ResourceUID::INVALID_ID) {
///     s += " uid=\"" + ResourceUID::get_singleton()->id_to_text(uid) + "\"";
/// }
/// </code>
/// An <c>x:Uids</c> entry, or a <c>Resource()</c> reference with a uid, whose path is a renamed one takes the new file's
/// uid, or loses it when that file has none; otherwise the old uid would still load the old file.
/// </summary>
internal static class XscnRenameUids
{
    /// <summary>Retargets the uids of the renamed paths in a rewritten document.</summary>
    /// <param name="document">The document after the path rewrite.</param>
    /// <param name="targets">Each new path as the document spells it, absolute or relative, to its <c>res://</c> path.</param>
    /// <returns>The document with those uids replaced or removed.</returns>
    public static XscnDocument Retarget(XscnDocument document, IReadOnlyDictionary<string, string> targets)
    {
        var session = new Session(targets);
        return new XscnReferenceWalk(session.Reference, file => file).Map(document with { Uids = session.Table(document.Uids) });
    }

    private sealed class Session(IReadOnlyDictionary<string, string> targets)
    {
        private readonly Dictionary<string, string?> uids = new(StringComparer.Ordinal);

        // A renamed path keeps one entry, with the new file's uid; an entry for a file with no uid is dropped.
        public ImmutableArray<XscnUid> Table(ImmutableArray<XscnUid> table)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<XscnUid>();
            foreach (XscnUid entry in table)
            {
                string? uid = targets.TryGetValue(entry.Path, out string? target) ? UidOf(target) : entry.Uid;
                if (uid is not null && seen.Add(entry.Path))
                {
                    kept.Add(entry with { Uid = uid });
                }
            }

            return [.. kept];
        }

        // A reference written with a uid takes the new file's; one written without stays so.
        public GodotPathRef Reference(GodotPathRef reference) =>
            reference is { Uid: not null, Path: { } path } && targets.TryGetValue(path, out string? target)
                ? new GodotPathRef(UidOf(target), path)
                : reference;

        // The uid text of the file at the path, or null when it has none; asked once per path.
        private string? UidOf(string path)
        {
            if (!uids.TryGetValue(path, out string? uid))
            {
                long id = ResourceLoader.GetResourceUid(path);
                uid = id == ResourceUid.InvalidId ? null : ResourceUid.IdToText(id);
                uids[path] = uid;
            }

            return uid;
        }
    }
}
