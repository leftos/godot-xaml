using System;
using System.Collections.Concurrent;
using System.Linq;
using Godot;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Addon;

/// <summary>
/// The Godot metadata the loader, the saver and the preview read and write a file with: the version its <c>x:Godot</c>
/// marker names when the addon carries it, else the newest version the addon carries. Each version is read from the
/// core's assembly on first use.
/// </summary>
internal static class XscnMetadata
{
    private static readonly string Newest =
        GodotMetadata.BundledVersions.Where(v => System.Version.TryParse(v, out _)).MaxBy(v => System.Version.Parse(v))
        ?? throw new InvalidOperationException("The core's assembly carries no metadata.");

    private static readonly ConcurrentDictionary<string, Lazy<GodotMetadata>> Loaded = new(StringComparer.Ordinal);

    // The marker values already warned about, so each is warned about once per editor session.
    private static readonly ConcurrentDictionary<string, bool> Warned = new(StringComparer.Ordinal);

    /// <summary>The metadata of an unmarked file: the newest version the addon carries, loaded once per assembly load.</summary>
    public static GodotMetadata Value => Of(Newest);

    /// <summary>
    /// The metadata a file is read and written with. A marker naming a version the addon does not carry reads with
    /// <see cref="Value"/>, and the first such file of each marker value pushes a warning naming both versions.
    /// </summary>
    /// <param name="marker">The file's <c>x:Godot</c> marker, or null when it has none.</param>
    /// <param name="path">The file the warning names, or null for a warning that names none.</param>
    /// <returns>The metadata.</returns>
    public static GodotMetadata For(string? marker, string? path = null)
    {
        if (marker is null)
        {
            return Value;
        }

        if (Carries(marker))
        {
            return Of(marker);
        }

        if (Warned.TryAdd(marker, true))
        {
            string where = path is null ? "godot-xaml: " : $"{path}: godot-xaml: ";
            GD.PushWarning(where + XscnMarker.NotCarried(marker, Newest));
        }

        return Value;
    }

    /// <summary>Whether a file with this marker can be written without changing its marker: it has none, or the addon carries it.</summary>
    /// <param name="marker">The file's <c>x:Godot</c> marker, or null when it has none.</param>
    /// <returns>True for no marker or a carried version.</returns>
    public static bool Carries(string? marker) => marker is null || GodotMetadata.BundledVersions.Contains(marker, StringComparer.Ordinal);

    /// <summary>The refusal a write pushes for a file whose marker the addon does not carry, naming both versions.</summary>
    /// <param name="path">The file.</param>
    /// <param name="marker">The file's marker.</param>
    /// <returns>The refusal's text.</returns>
    public static string NotWritten(string path, string marker) => $"{path}: godot-xaml: " + XscnMarker.NotWritten(marker, Newest);

    private static GodotMetadata Of(string version) =>
        Loaded.GetOrAdd(version, v => new Lazy<GodotMetadata>(() => GodotMetadata.LoadBundled(v))).Value;
}
