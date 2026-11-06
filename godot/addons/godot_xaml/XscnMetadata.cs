using System;
using System.Collections.Concurrent;
using System.Linq;
using Godot;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Addon;

/// <summary>
/// The Godot metadata the loader, the saver and the preview read and write a file with: the version its <c>x:Godot</c>
/// marker names when the addon carries it, else the running engine's major.minor when the addon carries that, else the
/// newest version the addon carries. Each version is read from the core's assembly on first use.
/// </summary>
internal static class XscnMetadata
{
    private static readonly string Newest = GodotMetadata.NewestVersion(GodotMetadata.BundledVersions);

    private static readonly ConcurrentDictionary<string, Lazy<GodotMetadata>> Loaded = new(StringComparer.Ordinal);

    // The marker values, and the running versions ("engine:" + version), already warned about, so each is warned about
    // once per editor session.
    private static readonly ConcurrentDictionary<string, bool> Warned = new(StringComparer.Ordinal);

    // The engine's major.minor, asked once: it does not change while the editor runs.
    private static readonly Lazy<string> EngineVersion = new(AskEngine);

    /// <summary>
    /// The running engine's major.minor (<c>4.8</c>); a seam the headless checks set to read and write as another version
    /// would, and set back to <see cref="EngineMinor"/> after. Each use calls it again.
    /// </summary>
    internal static Func<string> RunningVersion { get; set; } = EngineMinor;

    /// <summary>
    /// The metadata of an unmarked file: the running engine's version when the addon carries it, else the newest version it
    /// carries, and the first such use of each running version pushes a warning naming both versions.
    /// </summary>
    public static GodotMetadata Value => Of(Running());

    /// <summary>
    /// The metadata a file is read and written with: its marker's version when the addon carries it, else
    /// <see cref="Value"/>. A marker naming a version the addon does not carry reads with <see cref="Value"/>, and the first
    /// such file of each marker value pushes a warning naming the marker and the version used.
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

        GodotMetadata used = Value;
        if (Warned.TryAdd(marker, true))
        {
            string where = path is null ? "godot-xaml: " : $"{path}: godot-xaml: ";
            GD.PushWarning(where + XscnMarker.NotCarried(marker, used.Version));
        }

        return used;
    }

    /// <summary>Whether a file with this marker can be written without changing its marker: it has none, or the addon carries it.</summary>
    /// <param name="marker">The file's <c>x:Godot</c> marker, or null when it has none.</param>
    /// <returns>True for no marker or a carried version.</returns>
    public static bool Carries(string? marker) => marker is null || GodotMetadata.BundledVersions.Contains(marker, StringComparer.Ordinal);

    /// <summary>
    /// The refusal a write pushes for a file whose marker the addon does not carry, naming the marker and the version of
    /// <see cref="Value"/>, which a write would stamp.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="marker">The file's marker.</param>
    /// <returns>The refusal's text.</returns>
    public static string NotWritten(string path, string marker) => $"{path}: godot-xaml: " + XscnMarker.NotWritten(marker, Value.Version);

    /// <summary>The running engine's major.minor, from <c>Engine.GetVersionInfo()</c>, asked once per assembly load.</summary>
    /// <returns>The version (<c>4.7</c>).</returns>
    internal static string EngineMinor() => EngineVersion.Value;

    // The version an unmarked file reads with: the running one when carried, else the newest, warned about once per value.
    private static string Running()
    {
        string running = RunningVersion();
        if (Carries(running))
        {
            return running;
        }

        if (Warned.TryAdd("engine:" + running, true))
        {
            GD.PushWarning($"godot-xaml: Godot {running} has no bundled metadata; using {Newest}");
        }

        return Newest;
    }

    // get_version_info's "major" and "minor" are ints (core/config/engine.cpp:153-160).
    private static string AskEngine()
    {
        using Godot.Collections.Dictionary info = Engine.GetVersionInfo();
        using Variant major = info["major"];
        using Variant minor = info["minor"];
        return $"{major.AsInt32()}.{minor.AsInt32()}";
    }

    private static GodotMetadata Of(string version) =>
        Loaded.GetOrAdd(version, v => new Lazy<GodotMetadata>(() => GodotMetadata.LoadBundled(v))).Value;
}
