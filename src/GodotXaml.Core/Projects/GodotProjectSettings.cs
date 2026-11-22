namespace GodotXaml.Core.Projects;

/// <summary>The Godot project settings in a <c>project.godot</c> that the tool reads without Godot.</summary>
public static class GodotProjectSettings
{
    private const string ApplicationSection = "application";

    private const string UseHiddenDataFolderKey = "config/use_hidden_project_data_directory";

    private const string FalseText = "false";

    /// <summary>
    /// Whether Godot keeps the project's data folder hidden, <c>.godot</c>, rather than visible, <c>godot</c>, from the
    /// <c>[application]</c> section's <c>config/use_hidden_project_data_directory</c>.
    /// </summary>
    /// <param name="projectGodotText">The text of a <c>project.godot</c>.</param>
    /// <returns>
    /// False only when the section's last assignment of the key is the literal <c>false</c>; an absent key, any other
    /// value, the key in another section, or a comment naming it gives true, Godot's default.
    /// </returns>
    public static bool UsesHiddenDataFolder(string projectGodotText)
    {
        bool inApplication = false;
        bool inString = false;
        string? assignment = null;
        foreach (string rawLine in projectGodotText.Split('\n'))
        {
            if (inString)
            {
                inString = RemainsInString(rawLine, inString: true);
                continue;
            }
            string line = rawLine.Trim();
            if (SectionOf(line) is { } section)
            {
                inApplication = string.Equals(section, ApplicationSection, StringComparison.Ordinal);
            }
            else if (inApplication && ValueOf(line) is { } value)
            {
                // The last assignment wins (project_settings.cpp:342-343), so the loop decides once it has them all.
                assignment = value;
            }
            inString = RemainsInString(rawLine, inString: false);
        }
        return assignment is null || !string.Equals(assignment, FalseText, StringComparison.Ordinal);
    }

    // The section a header line names: the text between `[` and the first `]`, trimmed, so `[application] ; note` and
    // `[ application ]` both name application (variant_parser.cpp:1758-1808). Null for a line that is no header.
    private static string? SectionOf(string line)
    {
        if (!line.StartsWith('['))
        {
            return null;
        }
        int end = line.IndexOf(']');
        return end < 0 ? null : line[1..end].Trim();
    }

    // The value a line assigns to the data folder setting: the text after `=`, cut at its first `;` comment and trimmed
    // (variant_parser.cpp:497-503, :1910). Null for a line that assigns another key, a longer one whose text starts the
    // same included.
    private static string? ValueOf(string line)
    {
        if (!line.StartsWith(UseHiddenDataFolderKey, StringComparison.Ordinal))
        {
            return null;
        }
        string rest = line[UseHiddenDataFolderKey.Length..].TrimStart();
        if (!rest.StartsWith('='))
        {
            return null;
        }
        string text = rest[1..];
        int comment = text.IndexOf(';');
        return (comment < 0 ? text : text[..comment]).Trim();
    }

    // Whether a quoted string is open at the end of a line, given whether one was open at its start: Godot writes a
    // String value's newlines as real ones (variant_parser.cpp:2032-2034), so the lines after an unfinished quote are
    // its text, never a header or a key. A backslash escapes the next character, so `\"` stays inside the string and
    // `\\` is an escaped backslash; a `;` outside a string starts a comment (variant_parser.cpp:1910).
    private static bool RemainsInString(string line, bool inString)
    {
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (!inString && character == ';')
            {
                return false;
            }
            if (inString && character == '\\')
            {
                index++;
            }
            else if (character == '"')
            {
                inString = !inString;
            }
        }
        return inString;
    }
}
