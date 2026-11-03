using Godot;

namespace GodotXaml.AddonChecks;

/// <summary>The resource an <c>.xres</c> root carries by script, holding a number the check reads back through C#.</summary>
[GlobalClass]
public partial class CheckedResource : Resource
{
    /// <summary>The number the check reads before and after a collection.</summary>
    [Export]
    public int Count { get; set; }
}
