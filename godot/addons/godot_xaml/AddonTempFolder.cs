using System;
using System.IO;
using Godot;

namespace GodotXaml.Addon;

/// <summary>The platforms whose temp folder rule differs.</summary>
internal enum TempFolderPlatform
{
    /// <summary>Windows, whose temp folder is already per user.</summary>
    Windows,

    /// <summary>Linux, whose temp folder is the shared <c>/tmp</c>.</summary>
    Linux,

    /// <summary>macOS, whose temp folder is the shared <c>/tmp</c> when <c>TMPDIR</c> is unset.</summary>
    MacOS,
}

/// <summary>
/// The per-user, private folder the load and save routes write their temp files in. A folder in the shared <c>/tmp</c>
/// would let another account create it first and block every load and save, or own it and read or swap the converted text
/// between the addon's write and Godot's read.
/// </summary>
internal static class AddonTempFolder
{
    private const string FolderName = "godot-xaml";

    private static readonly object Gate = new();

    private static string? cached;

    private static string? primaryOverride;

    /// <summary>
    /// The folder the routes write their temp files in: resolved on first use, created again on every call, so a folder the
    /// OS removed comes back. Only a usable resolution is reused; a cached fallback that has gone is resolved anew, never
    /// recreated by name.
    /// </summary>
    /// <returns>The folder, with forward slashes.</returns>
    internal static string Get()
    {
        lock (Gate)
        {
            if (cached is not null && Directory.Exists(cached) && TryCreateOwnerOnly(cached))
            {
                return cached;
            }

            string? primary = primaryOverride ?? PrimaryFolder(CurrentPlatform(), System.Environment.GetEnvironmentVariable, Path.GetTempPath());
            (string folder, string? reason) = ResolveFrom(primary, ProjectSettings.GlobalizePath("res://"));
            if (reason is not null)
            {
                GD.PushWarning($"godot-xaml: the temp folder {primary ?? "(none)"} cannot be used ({reason}); using {folder} for this process.");
            }

            cached = folder.Replace('\\', '/');
            return cached;
        }
    }

    /// <summary>
    /// Forgets the cached folder and makes <see cref="Get"/> resolve from the given primary instead of the environment's, so
    /// the checks can stage a folder to remove without touching the one other processes of this user share.
    /// </summary>
    /// <param name="primary">The primary <see cref="Get"/> resolves from, or null to resolve from the environment again.</param>
    internal static void ResetForChecks(string? primary)
    {
        lock (Gate)
        {
            cached = null;
            primaryOverride = primary;
        }
    }

    /// <summary>The per-user folder the platform's rule names. Pure: no I/O.</summary>
    /// <param name="platform">The platform whose rule applies.</param>
    /// <param name="getEnv">Reads an environment variable, returning null for an unset one.</param>
    /// <param name="tempPath">The OS temp folder, which only the Windows rule uses.</param>
    /// <returns>The folder, or null when the rule names none (<c>HOME</c> unset or relative on Linux or macOS).</returns>
    internal static string? PrimaryFolder(TempFolderPlatform platform, Func<string, string?> getEnv, string tempPath)
    {
        if (platform == TempFolderPlatform.Windows)
        {
            return Path.Combine(tempPath, FolderName);
        }

        // Unix paths are joined with '/' so the checks can run the Linux and macOS rules on Windows.
        string? home = UnixFullyQualified(getEnv("HOME"));
        return platform == TempFolderPlatform.MacOS
            ? UnixJoin(home, $"Library/Caches/{FolderName}")
            : UnixJoin(UnixFullyQualified(getEnv("XDG_CACHE_HOME")) ?? UnixJoin(home, ".cache"), FolderName);
    }

    private static string? UnixJoin(string? folder, string tail) => folder is null ? null : $"{folder.TrimEnd('/')}/{tail}";

    /// <summary>
    /// Resolves the folder from a given primary without the cache: the primary when it is absolute, outside the project, and
    /// this user can make it owner-only and write in it; else a fresh private folder for this process.
    /// </summary>
    /// <param name="primary">The per-user folder, or null when the rule named none.</param>
    /// <param name="projectRoot">The project's root folder, inside which the folder must not be.</param>
    /// <returns>The folder, and why the primary was not used, or null when it was.</returns>
    internal static (string Folder, string? FallbackReason) ResolveFrom(string? primary, string projectRoot)
    {
        string? reason = Rejection(primary, projectRoot);
        if (reason is null)
        {
            return (primary!, null);
        }

        string fallback = Directory.CreateTempSubdirectory(FolderName + "-").FullName;
        return (fallback, reason);
    }

    // Why the primary cannot be used, or null once it is created owner-only and a probe file was written and removed in it.
    private static string? Rejection(string? primary, string projectRoot)
    {
        if (primary is null)
        {
            return "no per-user folder: HOME is unset or relative";
        }

        if (!Path.IsPathFullyQualified(primary))
        {
            return "it is not an absolute path";
        }

        // An exported game has no project folder on disk: res:// globalizes to "" there.
        if (projectRoot.Length > 0 && IsUnder(primary, projectRoot))
        {
            return $"it is inside the project {projectRoot}";
        }

        try
        {
            CreateOwnerOnly(primary);
            // Named as the routes name their files, so a probe a crash leaves behind is caught by the load route's prune.
            string probe = Path.Combine(primary, $"{Guid.NewGuid():N}.tres");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return exception.Message;
        }
    }

    // A cached folder that can no longer be made owner-only is resolved anew rather than failing the load or save.
    private static bool TryCreateOwnerOnly(string folder)
    {
        try
        {
            CreateOwnerOnly(folder);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the folder, or keeps it, owner-only: on Unix CreateDirectory leaves an existing folder's mode alone, so the
    /// mode is set as well; only the owner may set it, which rejects a folder another account made. On Windows the
    /// profile's inherited ACL applies. The preview server's discovery folders are made the same way.
    /// </summary>
    /// <param name="folder">The folder, a full path.</param>
    internal static void CreateOwnerOnly(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(folder, ownerOnly);
        File.SetUnixFileMode(folder, ownerOnly);
    }

    // The trailing separator keeps /a/bc from counting as under /a/b.
    private static bool IsUnder(string path, string root)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string full = WithTrailingSeparator(Path.GetFullPath(path));
        return full.StartsWith(WithTrailingSeparator(Path.GetFullPath(root)), comparison);
    }

    private static string WithTrailingSeparator(string path) => path.Replace('\\', '/').TrimEnd('/') + "/";

    // What Path.IsPathFullyQualified answers on Unix, whichever system runs the rule: Windows would reject "/home/u".
    private static string? UnixFullyQualified(string? path) => path is not null && path.StartsWith('/') ? path : null;

    private static TempFolderPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? TempFolderPlatform.Windows
        : OperatingSystem.IsMacOS() ? TempFolderPlatform.MacOS
        : TempFolderPlatform.Linux;
}
