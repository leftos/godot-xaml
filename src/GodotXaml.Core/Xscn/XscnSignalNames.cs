using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using GodotXaml.Core.Metadata;

namespace GodotXaml.Core.Xscn;

/// <summary>
/// Maps a signal's C# name, as an attribute or <c>x:Connect</c> names it, to its Godot name and back: by the emitter's class
/// when it is known, else by the one Godot name every class with a signal of that C# name agrees on. The reader and the
/// writer share it, with the names of <c>Object.ConnectFlags</c>.
/// </summary>
internal static class XscnSignalNames
{
    // Building the lookups scans every class's signals, so each metadata's are built once, on their first use.
    private static readonly ConditionalWeakTable<GodotMetadata, Tables> TablesByMetadata = new();

    /// <summary>The Godot name of the signal a C# name gives.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="cls">The emitter's class, or null when it is not known.</param>
    /// <param name="csName">The name as written.</param>
    /// <returns>
    /// The signal's Godot name on the class or a class it inherits; on an unknown class, the one Godot name every class's
    /// signal of that C# name has; else the name verbatim, a script's own signal.
    /// </returns>
    public static string GodotName(GodotMetadata metadata, GodotClassInfo? cls, string csName) =>
        (cls is null ? TablesOf(metadata).Agreed.GetValueOrDefault(csName) : metadata.FindSignalByCSharpName(cls, csName)?.Name) ?? csName;

    /// <summary>The signal an attribute of a node of known class names.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="cls">The node's class.</param>
    /// <param name="name">The attribute's name.</param>
    /// <returns>
    /// The signal of that C# name on the class or one it inherits, unless the class has a property of that name or the name
    /// is dotted; else null.
    /// </returns>
    public static GodotSignalInfo? SignalAttribute(GodotMetadata metadata, GodotClassInfo cls, string name) =>
        name.Contains('.', StringComparison.Ordinal) || metadata.FindPropertyByCSharpName(cls, name) is not null
            ? null
            : metadata.FindSignalByCSharpName(cls, name);

    /// <summary>The C# name of a signal of a known class that reads back as the same signal.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="cls">The emitter's class.</param>
    /// <param name="godotName">The signal's Godot name.</param>
    /// <returns>The C# name of the class's or an ancestor's signal of that Godot name, or null when there is none.</returns>
    public static string? OnClass(GodotMetadata metadata, GodotClassInfo cls, string godotName)
    {
        string? csName = FindSignal(metadata, cls, godotName)?.CSharpName;
        return csName is not null && GodotName(metadata, cls, csName) == godotName ? csName : null;
    }

    /// <summary>The name <c>x:Connect</c>'s <c>Signal</c> holds for a signal.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="cls">The emitter's class, or null when it is not known.</param>
    /// <param name="godotName">The signal's Godot name.</param>
    /// <returns>The C# name that <see cref="GodotName"/> maps back to <paramref name="godotName"/>, else the Godot name verbatim.</returns>
    public static string ConnectName(GodotMetadata metadata, GodotClassInfo? cls, string godotName)
    {
        string? csName = cls is null ? TablesOf(metadata).AgreedByGodotName.GetValueOrDefault(godotName) : OnClass(metadata, cls, godotName);
        return csName ?? godotName;
    }

    /// <summary>The names of <c>Object.ConnectFlags</c>, GodotSharp's own; none when the metadata lacks the enum, leaving integers.</summary>
    /// <param name="metadata">The metadata.</param>
    /// <returns>The flag names.</returns>
    public static XscnEnumNames ConnectFlags(GodotMetadata metadata) => TablesOf(metadata).ConnectFlags;

    private static Tables TablesOf(GodotMetadata metadata) => TablesByMetadata.GetValue(metadata, Build);

    private static Tables Build(GodotMetadata metadata)
    {
        FrozenDictionary<string, string?> agreed = BuildAgreed(metadata);
        return new Tables(agreed, BuildReverse(agreed), BuildConnectFlags(metadata));
    }

    // Each C# signal name, and the one Godot name every class's signal of that C# name has, or null when they differ.
    private static FrozenDictionary<string, string?> BuildAgreed(GodotMetadata metadata) =>
        metadata
            .Classes.SelectMany(c => c.Signals)
            .Where(s => s.CSharpName is not null)
            .GroupBy(s => s.CSharpName!, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => SingleName(g.Select(s => s.Name)), StringComparer.Ordinal);

    // Each agreed Godot name and the ordinally first C# name that maps to it.
    private static FrozenDictionary<string, string> BuildReverse(FrozenDictionary<string, string?> agreed) =>
        agreed
            .Where(pair => pair.Value is not null)
            .GroupBy(pair => pair.Value!, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => g.Select(pair => pair.Key).Order(StringComparer.Ordinal).First(), StringComparer.Ordinal);

    private static XscnEnumNames BuildConnectFlags(GodotMetadata metadata) =>
        new([.. metadata.FindEnum("Object.ConnectFlags")?.Members.Select(m => new XscnEnumName(m.CSharpName, m.Value)) ?? []], isFlags: true);

    private static string? SingleName(IEnumerable<string> names)
    {
        List<string> distinct = [.. names.Distinct(StringComparer.Ordinal).Take(2)];
        return distinct.Count == 1 ? distinct[0] : null;
    }

    // The nearest signal of a Godot name on the class or one of its ancestors.
    private static GodotSignalInfo? FindSignal(GodotMetadata metadata, GodotClassInfo cls, string godotName)
    {
        for (GodotClassInfo? current = cls; current is not null; current = current.Parent is null ? null : metadata.FindClass(current.Parent))
        {
            GodotSignalInfo? signal = current.Signals.FirstOrDefault(s => s.Name == godotName);
            if (signal is not null)
            {
                return signal;
            }
        }

        return null;
    }

    /// <summary>One metadata's signal lookups.</summary>
    /// <param name="Agreed">C# name to the agreed Godot name, or null when the classes disagree.</param>
    /// <param name="AgreedByGodotName">Agreed Godot name to its ordinally first C# name.</param>
    /// <param name="ConnectFlags">The names of <c>Object.ConnectFlags</c>.</param>
    private sealed record Tables(
        FrozenDictionary<string, string?> Agreed,
        FrozenDictionary<string, string> AgreedByGodotName,
        XscnEnumNames ConnectFlags
    );
}
