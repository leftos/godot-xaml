namespace GodotXaml.LanguageServer.Tests;

/// <summary>The repository the tests run from, found by walking up from the test binary to GodotXaml.slnx.</summary>
internal static class RepoRoot
{
    /// <summary>The repository's root folder.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The full path of a file or folder given relative to the repository root.</summary>
    /// <param name="parts">The path's segments below the root.</param>
    /// <returns>The full path.</returns>
    public static string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "GodotXaml.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName
            ?? throw new InvalidOperationException($"No GodotXaml.slnx above {AppContext.BaseDirectory}; run the tests from the repository.");
    }
}
