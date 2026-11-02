using System;
using GodotXaml.Core.Metadata;

namespace GodotXaml.Addon;

/// <summary>The Godot 4.7 metadata the loader and the saver share, read from the core's assembly on first use.</summary>
internal static class XscnMetadata
{
    private static readonly Lazy<GodotMetadata> Bundled = new(() => GodotMetadata.LoadBundled("4.7"));

    /// <summary>The metadata, loaded once per assembly load.</summary>
    public static GodotMetadata Value => Bundled.Value;
}
