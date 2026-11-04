using System.Collections.Immutable;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// The lists one conversion's by-source families are ordered by (<see cref="DynamicPropertyFamilies.EntryCreation.BySource"/>):
/// a Shader's local uniforms for a ShaderMaterial's parameters, an ArrayMesh's blend shape names for a MeshInstance3D's
/// blend shapes. Each resource is read once and cached for the conversion, since both sorts of a section ask for the same
/// list. A sibling value that is an inline resource is read directly, and a key reference through the document's
/// <c>x:Resources</c> table. An external file is read through the host's <see cref="ISceneFiles"/>, once per conversion
/// for each referrer, reference and uid: a <c>.gdshader</c>'s whole text is the shader's code, and a text <c>.tres</c>
/// ArrayMesh holds its blend shape names; the extension decides, compared case-insensitively as Godot's
/// <c>ResourceFormatLoader::recognize_path</c> does (<c>nocasecmp</c>, core/io/resource_loader.cpp), and a file of any
/// other extension is not asked for. No list comes from a file when the conversion has no reader or no key for its own
/// file, or the reader cannot give it, nor from any other value, a resource that does not hold the list, or a shader with a
/// preprocessor directive; a family with no list keeps source order.
/// </summary>
/// <param name="table">The document's <c>x:Resources</c> entries by key; read on each lookup, so it may be filled after.</param>
/// <param name="files">The host's file reader, or null when it gives none.</param>
/// <param name="sourceKey">The key of the file being converted, the referrer of its references; null when unknown.</param>
/// <param name="uidOf">
/// The <c>uid://</c> the document's <c>x:Uids</c> gives a path, or null; never one discovery collects as it goes, so each
/// sort of a section reads under the same key.
/// </param>
internal sealed class SourceOrders(
    IReadOnlyDictionary<string, XscnResource> table,
    ISceneFiles? files,
    string? sourceKey,
    Func<string, string?> uidOf
)
{
    private const string CodeProperty = "code";
    private const string BlendShapeNamesProperty = "_blend_shape_names";
    private const string ArrayMeshClass = "ArrayMesh";

    private readonly Dictionary<XscnResource, SourceList?> shaders = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<XscnResource, SourceList?> meshes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(string Referrer, string Reference, string? Uid), SourceList?> shaderFiles = [];
    private readonly Dictionary<(string Referrer, string Reference, string? Uid), SourceList?> meshFiles = [];

    /// <summary>A cache over no document and no reader, under which every key reference and file gives no list.</summary>
    /// <returns>The cache.</returns>
    public static SourceOrders Empty() => new(new Dictionary<string, XscnResource>(StringComparer.Ordinal), null, null, _ => null);

    /// <summary>The list a sibling property's value gives, or null when it gives none.</summary>
    /// <param name="value">The sibling property's value.</param>
    /// <param name="read">What to read from the resource.</param>
    /// <returns>The list, or null.</returns>
    public SourceList? ListOf(XscnValue value, DynamicPropertyFamilies.SourceRead read)
    {
        // .xscn text holds a res:// file as an XscnFile, and a relative one as Resource("…") value text.
        switch (value)
        {
            case XscnFile file:
                return FileListOf(file.Path, uidOf(file.Path), read);
            case XscnScalar { Value: GodotPathRef { Path: { } path } reference }:
                return FileListOf(path, uidOf(path) ?? reference.Uid, read);
        }

        XscnResource? resource = value switch
        {
            XscnInline inline => inline.Resource,
            XscnKeyRef key => table.GetValueOrDefault(key.Key),
            _ => null,
        };
        if (resource is null)
        {
            return null;
        }

        Dictionary<XscnResource, SourceList?> cache = read == DynamicPropertyFamilies.SourceRead.ShaderUniforms ? shaders : meshes;
        if (!cache.TryGetValue(resource, out SourceList? list))
        {
            list = read == DynamicPropertyFamilies.SourceRead.ShaderUniforms ? ShaderList(resource) : BlendShapeList(resource);
            cache.Add(resource, list);
        }

        return list;
    }

    // The list a file gives, read once per referrer, reference and uid (x:Uids' for the path, else the reference's own); a
    // file whose extension cannot hold the list as text is not asked for.
    private SourceList? FileListOf(string path, string? uid, DynamicPropertyFamilies.SourceRead read)
    {
        bool shader = read == DynamicPropertyFamilies.SourceRead.ShaderUniforms;
        if (files is null || sourceKey is null || !HasExtension(path, shader ? ".gdshader" : ".tres"))
        {
            return null;
        }

        Dictionary<(string Referrer, string Reference, string? Uid), SourceList?> cache = shader ? shaderFiles : meshFiles;
        (string, string, string?) key = (sourceKey, path, uid);
        if (!cache.TryGetValue(key, out SourceList? list))
        {
            list = files.Read(key.Item1, key.Item2, key.Item3) is { } file ? ListOfText(file.Text, shader) : null;
            cache.Add(key, list);
        }

        return list;
    }

    private static SourceList? ListOfText(string text, bool shader) => shader ? ShaderListOf(text) : MeshFileList(text);

    private static bool HasExtension(string path, string extension) => Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase);

    // A text resource file whose [resource] section is an ArrayMesh gives its _blend_shape_names; a file the text reader
    // rejects, or of another class, gives none.
    private static SourceList? MeshFileList(string text)
    {
        TscnDocument document;
        try
        {
            document = TscnReader.Read(text);
        }
        catch (TscnParseException)
        {
            return null;
        }

        if (document is not { Kind: TscnKind.Resource, Header.Type: ArrayMeshClass })
        {
            return null;
        }

        TscnProperty? names = document.ResourceProperties.LastOrDefault(property => property.Name == BlendShapeNamesProperty);
        return names?.Value is GodotPackedStrings packed ? new SourceList(packed.Items) : null;
    }

    // Shader::code is the shader's text (shader.cpp); a resource with none, or one the scan cannot read, gives no list.
    private static SourceList? ShaderList(XscnResource shader) =>
        LastValue(shader, CodeProperty) is GodotString code ? ShaderListOf(code.Value) : null;

    // The uniforms of a shader's text, or null when only the preprocessor could tell them.
    private static SourceList? ShaderListOf(string code) => ShaderUniforms.Scan(code) is { } names ? new SourceList(names) : null;

    // ArrayMesh stores its blend shape names in _blend_shape_names (mesh.cpp); a mesh without them gives no list.
    private static SourceList? BlendShapeList(XscnResource mesh) =>
        LastValue(mesh, BlendShapeNamesProperty) is GodotPackedStrings names ? new SourceList(names.Items) : null;

    // The scalar value of the last property of that name, since each set replaces the last; null when there is none.
    private static GodotValue? LastValue(XscnResource resource, string property)
    {
        for (int index = resource.Properties.Length - 1; index >= 0; index--)
        {
            if (resource.Properties[index] is { GodotName: var name, Value: XscnScalar scalar } && name == property)
            {
                return scalar.Value;
            }
        }

        return null;
    }
}

/// <summary>A by-source family's list: each name's index in it (the first, for a repeated name) and its length.</summary>
internal sealed class SourceList
{
    private readonly Dictionary<string, int> places;
    private readonly int length;

    /// <summary>Creates the list.</summary>
    /// <param name="names">The names in Godot's order.</param>
    public SourceList(ImmutableArray<string> names)
    {
        places = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
        for (int index = 0; index < names.Length; index++)
        {
            places.TryAdd(names[index], index);
        }

        length = names.Length;
    }

    /// <summary>The name's index, or the list's length for a name it lacks, which goes after the listed ones.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The place.</returns>
    public int PlaceOf(string name) => places.TryGetValue(name, out int place) ? place : length;
}
