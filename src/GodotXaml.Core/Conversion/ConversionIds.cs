using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// The section ids of a converted .tscn or .tres: each ext_resource's by its path, each sub_resource's by the
/// <see cref="XscnResource"/> it became (the same object, not an equal one).
/// </summary>
public sealed class ConversionIds
{
    private readonly Dictionary<string, string> extIds;
    private readonly Dictionary<XscnResource, string> subIds;

    internal ConversionIds(Dictionary<string, string> extIds, Dictionary<XscnResource, string> subIds)
    {
        this.extIds = new Dictionary<string, string>(extIds, StringComparer.Ordinal);
        this.subIds = new Dictionary<XscnResource, string>(subIds, ReferenceEqualityComparer.Instance);
    }

    /// <summary>The id of the ext_resource at a path.</summary>
    /// <param name="path">The <c>res://</c> path.</param>
    /// <returns>The id, or null when the source had no ext_resource at that path.</returns>
    public string? ExtIdOf(string path) => extIds.GetValueOrDefault(path);

    /// <summary>The id of the sub_resource a resource of the converted document came from.</summary>
    /// <param name="resource">The resource, as the converted document holds it.</param>
    /// <returns>The id, or null when the resource did not come from a sub_resource.</returns>
    public string? SubIdOf(XscnResource resource) => subIds.GetValueOrDefault(resource);
}
