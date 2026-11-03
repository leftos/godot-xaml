using Godot;

namespace GodotXaml.AddonChecks;

/// <summary>A global class two script levels above Resource, so the loader's walk of global class bases is checked.</summary>
[GlobalClass]
public partial class DerivedCheckedResource : CheckedResource { }
