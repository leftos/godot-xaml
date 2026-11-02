using GodotXaml.Core.Tscn;

namespace GodotXaml.Cli;

/// <summary>A file the run leaves as text because Godot loads it before any addon exists.</summary>
/// <param name="Path">The file's full path, as the run found it.</param>
/// <param name="Reason">The project setting naming it, or the file that reaches it.</param>
/// <param name="Unread">Why the file's references could not be followed, or null when they were.</param>
/// <param name="Listed">Whether the file is one of the run's sources, so it is printed and left out of the run.</param>
internal sealed record LeftFile(string Path, string Reason, string? Unread = null, bool Listed = true);

/// <summary>The files of a project Godot loads before any addon, which stay as text.</summary>
internal static class StartupFiles
{
    private const string SettingsFile = "project.godot";
    private const string ResPrefix = "res://";
    private const string UidPrefix = "uid://";
    private const string BusLayoutSetting = "audio/buses/default_bus_layout";
    private const string BusLayoutDefault = "res://default_bus_layout.tres";
    private static readonly string[] StartupExtensions = [".tscn", ".tres"];

    // Godot loads these before it adds the addon's loaders (main/main.cpp:3691, 3847-3849, 3880-3881,
    // scene/main/scene_tree.cpp:2194, all before main.cpp:4494), so a file one names that this run converts to .xscn or
    // .xres has no loader at the moment Godot wants it. The bus layout's default file is loaded only when no untagged
    // key names the setting (audio_server.cpp:1642-1650).
    private static readonly string[] Settings =
    [
        "gui/theme/custom",
        "gui/theme/custom_font",
        BusLayoutSetting,
        "rendering/environment/defaults/default_environment",
        "internationalization/locale/translations",
        "display/mouse_cursor/custom_image",
    ];

    /// <summary>The files of a project Godot loads before any addon, which stay as text.</summary>
    /// <param name="root">The project root, which holds project.godot.</param>
    /// <param name="sources">Every forward source the run found, as full paths.</param>
    /// <returns>The left files, in the order found.</returns>
    public static List<LeftFile> Find(string root, IReadOnlyList<string> sources)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(sources);
        var sourcePaths = new Dictionary<string, string>(InputFiles.PathComparer);
        foreach (string source in sources)
        {
            sourcePaths.TryAdd(Path.GetFullPath(source), source);
        }
        var uids = new Lazy<Dictionary<string, string>>(() => UidMap(sources));
        var queue = new Queue<(string File, string Reason)>();
        foreach ((string setting, string value) in NamedValues(Path.Combine(root, SettingsFile)))
        {
            string? target = Resolve(root, value, uids);
            if (target is not null && IsStartupResource(target))
            {
                queue.Enqueue((target, setting));
            }
        }
        return Reach(root, queue, sourcePaths, uids);
    }

    // Every file a setting names and every file those reach, breadth first and once: a left theme whose reference was
    // converted fails whole at startup (resource_format_text.cpp:147-160). A file is listed, so printed and left out of
    // the run, only when it is one of the run's sources; one under addons/ or outside the walk is still followed.
    private static List<LeftFile> Reach(
        string root,
        Queue<(string File, string Reason)> queue,
        Dictionary<string, string> sourcePaths,
        Lazy<Dictionary<string, string>> uids
    )
    {
        var left = new List<LeftFile>();
        var walked = new HashSet<string>(InputFiles.PathComparer);
        while (queue.Count > 0)
        {
            (string file, string reason) = queue.Dequeue();
            if (!walked.Add(file))
            {
                continue;
            }
            TscnDocument? document = Read(file, out string? unread);
            bool listed = sourcePaths.TryGetValue(file, out string? path);
            left.Add(new LeftFile(listed ? path! : file, reason, unread, listed));
            if (document is not null)
            {
                EnqueueReached(root, file, document, queue, uids);
            }
        }
        return left;
    }

    private static void EnqueueReached(
        string root,
        string file,
        TscnDocument document,
        Queue<(string File, string Reason)> queue,
        Lazy<Dictionary<string, string>> uids
    )
    {
        foreach (TscnExtResource resource in document.ExtResources)
        {
            string reached = Referenced(root, file, resource, uids);
            if (IsStartupResource(reached))
            {
                queue.Enqueue((reached, $"through {ProjectRoot.ResPath(root, file)}"));
            }
        }
    }

    // The file an ext_resource names: its uid when the map holds one, which Godot prefers
    // (resource_format_text.cpp:492-496), else its path, a res:// path under the root or one relative to the file's own
    // folder.
    private static string Referenced(string root, string file, TscnExtResource resource, Lazy<Dictionary<string, string>> uids)
    {
        if (resource.Uid is string uid && uids.Value.GetValueOrDefault(uid) is string byUid)
        {
            return byUid;
        }
        return resource.Path.StartsWith(ResPrefix, StringComparison.Ordinal)
            ? Path.GetFullPath(Path.Combine(root, resource.Path[ResPrefix.Length..]))
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, resource.Path));
    }

    // The path a setting's value names, by its res:// path or by the uid map; null when it names nothing.
    private static string? Resolve(string root, string value, Lazy<Dictionary<string, string>> uids)
    {
        if (value.StartsWith(ResPrefix, StringComparison.Ordinal))
        {
            return Path.GetFullPath(Path.Combine(root, value[ResPrefix.Length..]));
        }
        return value.StartsWith(UidPrefix, StringComparison.Ordinal) ? uids.Value.GetValueOrDefault(value) : null;
    }

    // A file the run can leave as text: an existing scene or resource, the only kind with references to follow. A font,
    // an image or a .res has none, and a file that does not exist is not Godot's to load either.
    private static bool IsStartupResource(string path) =>
        File.Exists(path) && StartupExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // The document of a .tscn or .tres, and why its references could not be followed when it has none. The file stays as
    // text either way, so the failure is reported rather than failing the run.
    private static TscnDocument? Read(string file, out string? unread)
    {
        unread = null;
        try
        {
            return TscnReader.Read(File.ReadAllText(file));
        }
        catch (TscnParseException error)
        {
            unread = error.Message;
        }
        catch (IOException error)
        {
            unread = error.Message;
        }
        catch (UnauthorizedAccessException error)
        {
            unread = error.Message;
        }
        return null;
    }

    // The settings a project file names, in file order: a section tag names the section, a key line names the setting,
    // and each quoted string on its value line is a value. Godot stores a key as section + "/" + key, or bare before
    // any section (project_settings.cpp:943-995), and takes a feature tag off the key (project_settings.cpp:324-340).
    // The bus layout's default file counts unless an untagged key names the setting, whatever its value.
    private static List<(string Setting, string Value)> NamedValues(string settingsFile)
    {
        List<(string Setting, string Value)> named = [];
        bool bareBusLayout = false;
        string? section = null;
        foreach (string line in File.ReadLines(settingsFile))
        {
            (string? nextSection, string? setting, string value, bool bareKey) = ReadSettingsLine(line, section);
            section = nextSection;
            if (setting is null)
            {
                continue;
            }
            bareBusLayout |= bareKey && string.Equals(setting, BusLayoutSetting, StringComparison.Ordinal);
            foreach (string text in Quoted(value))
            {
                if (text.Length > 0)
                {
                    named.Add((setting, text));
                }
            }
        }
        if (!bareBusLayout)
        {
            named.Add((BusLayoutSetting, BusLayoutDefault));
        }
        return named;
    }

    // One line of a project file: the section a [tag] sets, the setting an assignment names, its value text, and whether
    // the key carried no feature tag.
    private static (string? Section, string? Setting, string Value, bool BareKey) ReadSettingsLine(string line, string? section)
    {
        string text = line.Trim();
        if (IsBlank(text))
        {
            return (section, null, "", false);
        }
        if (IsSection(text))
        {
            return (text[1..^1], null, "", false);
        }
        int equals = text.IndexOf('=');
        if (equals < 0)
        {
            return (section, null, "", false);
        }
        (string? setting, bool bare) = SettingOf(text[..equals], section);
        return (section, setting, setting is null ? "" : text[(equals + 1)..], bare);
    }

    private static bool IsBlank(string text) => text.Length == 0 || text[0] == ';';

    private static bool IsSection(string text) => text.Length > 1 && text[0] == '[' && text[^1] == ']';

    // The setting a key names, with a feature tag taken off its end, and whether it had none; null for another setting.
    private static (string? Setting, bool BareKey) SettingOf(string keyText, string? section)
    {
        string key = keyText.Trim();
        int tag = key.IndexOf('.');
        bool bare = tag < 0;
        if (!bare)
        {
            key = key[..tag];
        }
        string setting = section is null ? key : section + "/" + key;
        return Array.IndexOf(Settings, setting) < 0 ? (null, false) : (setting, bare);
    }

    // Every quoted string on a value's line by a plain quote search: a one-string setting gives one, and
    // PackedStringArray("res://a.tres", "res://b.tres") gives both.
    private static List<string> Quoted(string value)
    {
        List<string> strings = [];
        int at = 0;
        while (true)
        {
            int open = value.IndexOf('"', at);
            int close = open < 0 ? -1 : value.IndexOf('"', open + 1);
            if (close < 0)
            {
                return strings;
            }
            strings.Add(value[(open + 1)..close]);
            at = close + 1;
        }
    }

    // The uid each forward source's header holds, so a uid:// value names the file that carries it. The first file with
    // a uid keeps it; a source that does not parse gives none here and the run reports it as any source.
    private static Dictionary<string, string> UidMap(IReadOnlyList<string> sources)
    {
        var uids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string source in sources)
        {
            string path = Path.GetFullPath(source);
            if (Read(path, out _)?.Header.Uid is string uid)
            {
                uids.TryAdd(uid, path);
            }
        }
        return uids;
    }
}
