using GodotXaml.Core.Values;

namespace GodotXaml.Cli;

/// <summary>
/// The global classes a Godot project's editor scan recorded, read from the project cache Godot 4.7.2 writes
/// (<c>ProjectSettings::store_global_class_list</c>, <c>core/config/project_settings.cpp</c>), so a back conversion can
/// write a resource header's <c>script_class</c>.
/// </summary>
internal static class GlobalClassCache
{
    private const string Folder = ".godot";
    private const string FileName = "global_script_class_cache.cfg";
    private const string ListKey = "list=";

    /// <summary>Reads a project's global class cache.</summary>
    /// <param name="root">The project root's full path.</param>
    /// <returns>
    /// A lookup from a script's <c>res://</c> path to its global class, or null when the project has no cache this reader
    /// can use: the core's GXC0106 warning then stands, and the header carries no <c>script_class</c>.
    /// </returns>
    internal static Func<string, string?>? Read(string root)
    {
        string? text = TryReadText(root);
        if (text is null || !TryListValue(text, out string valueText))
        {
            return null;
        }
        // The list is an Array of Dictionaries; a value of another kind says nothing about a class, so it gives no lookup
        // and the core's GXC0106 reports it as it does a cache that is absent.
        if (TryParse(valueText) is not GodotArray list)
        {
            return null;
        }
        Dictionary<string, string> classes = ClassesOf(list);
        return path => classes.GetValueOrDefault(path);
    }

    // The file's text, or null when there is none to read. A project the editor has never scanned has no cache, and Godot
    // itself assumes every class is still known (project_settings.cpp:1462-1464), so no lookup is the right answer there:
    // the core's GXC0106 says the lookup is missing, which is exactly the case.
    private static string? TryReadText(string root)
    {
        string path = Path.Combine(root, Folder, FileName);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The one value after the ConfigFile's only key. Godot writes the list under an empty section name, so no `[header]`
    // precedes it (core/io/config_file.cpp:132-134).
    private static bool TryListValue(string text, out string valueText)
    {
        string trimmed = text.TrimStart();
        if (!trimmed.StartsWith(ListKey, StringComparison.Ordinal))
        {
            valueText = "";
            return false;
        }
        valueText = trimmed[ListKey.Length..];
        return true;
    }

    // The value as Godot reads it, or null for text Godot does not read: a cache this reader cannot use gives no lookup,
    // so the core's GXC0106 reports it as it does a cache that is absent.
    private static GodotValue? TryParse(string valueText)
    {
        try
        {
            return GodotValueReader.Read(valueText);
        }
        catch (GodotValueParseException)
        {
            return null;
        }
    }

    // The class of each script path. An entry without both a path and a class is skipped, and a path listed twice keeps
    // the first class, as the addon's lookup answers with the first entry it meets.
    private static Dictionary<string, string> ClassesOf(GodotArray list)
    {
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (GodotValue item in list.Items)
        {
            if (item is GodotDictionary entry && ScriptPathOf(entry) is { } path && ClassOf(entry) is { } className)
            {
                classes.TryAdd(path, className);
            }
        }
        return classes;
    }

    // The value of the entry whose key carries this text; Godot writes the keys as quoted Strings.
    private static GodotValue? EntryValue(GodotDictionary entry, string key)
    {
        foreach (GodotEntry candidate in entry.Entries)
        {
            if (KeyText(candidate.Key) == key)
            {
                return candidate.Value;
            }
        }
        return null;
    }

    private static string? ScriptPathOf(GodotDictionary entry) => EntryValue(entry, "path") is GodotString path ? path.Value : null;

    private static string? ClassOf(GodotDictionary entry) =>
        EntryValue(entry, "class") switch
        {
            GodotStringName name => name.Value,
            GodotString text => text.Value,
            _ => null,
        };

    private static string? KeyText(GodotValue key) =>
        key switch
        {
            GodotString text => text.Value,
            GodotStringName name => name.Value,
            _ => null,
        };
}
