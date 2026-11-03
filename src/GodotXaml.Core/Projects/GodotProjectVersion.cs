using System.Text.RegularExpressions;

namespace GodotXaml.Core.Projects;

/// <summary>The Godot version a project names in its <c>project.godot</c>.</summary>
public static class GodotProjectVersion
{
    private const string Section = "[application]";

    private const string Key = "config/features";

    private static readonly Regex Quoted = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);

    private static readonly Regex MinorVersion = new(@"^\d+\.\d+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the Godot minor version from the <c>[application]</c> section's
    /// <c>config/features=PackedStringArray(...)</c>: its first element of the form <c>4.7</c>.
    /// </summary>
    /// <param name="projectGodotText">The text of a <c>project.godot</c>.</param>
    /// <returns>The version (<c>4.7</c>), or null when the section, the key or such an element is missing.</returns>
    public static string? Read(string projectGodotText)
    {
        bool inApplication = false;
        foreach (string rawLine in projectGodotText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inApplication = string.Equals(line, Section, StringComparison.Ordinal);
                continue;
            }
            if (!inApplication || !line.StartsWith(Key, StringComparison.Ordinal))
            {
                continue;
            }

            string rest = line[Key.Length..].TrimStart();
            if (!rest.StartsWith('='))
            {
                continue;
            }

            foreach (Match element in Quoted.Matches(rest))
            {
                string value = element.Groups[1].Value;
                if (MinorVersion.IsMatch(value))
                {
                    return value;
                }
            }
            return null;
        }
        return null;
    }
}
