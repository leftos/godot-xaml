using System;
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace GodotXaml.EditorChecks;

/// <summary>A stamp that changes when the game assembly reloads, read by the editor checks.</summary>
[Tool]
[GlobalClass]
public partial class ReloadProbe : RefCounted
{
    private static readonly string Stamp = Guid.NewGuid().ToString();

    /// <summary>The stamp of the assembly now loaded.</summary>
    /// <returns>The stamp.</returns>
    [SuppressMessage("Performance", "CA1822", Justification = "GDScript calls it on an instance, to read through a fresh object.")]
    public string GetStamp() => Stamp;
}
