using Godot;

namespace GodotXaml.AddonChecks;

/// <summary>What a GDScript hands its own resource to while that resource loads, kept for the check to read afterwards.</summary>
[GlobalClass]
public partial class CheckHandoff : RefCounted
{
    /// <summary>The resource the last load's script handed over, or null before a load.</summary>
    public static Resource? Held { get; set; }

    /// <summary>Keeps the resource the script passes.</summary>
    /// <param name="resource">The resource the calling script holds.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Godot exposes only instance methods to scripts, and the check's GDScript calls Take on a new instance."
    )]
    public void Take(Resource resource) => Held = resource;
}
