using System.Threading;
using Godot;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Xscn;
using FileAccess = Godot.FileAccess;

namespace GodotXaml.Addon;

/// <summary>Godot's loader for <c>.xscn</c> scenes and <c>.xres</c> resources, registered as a global class.</summary>
[GlobalClass]
[Tool]
public partial class XscnFormatLoader : ResourceFormatLoader
{
    private const string SceneExtension = "xscn";
    private const string ResourceExtension = "xres";

    private static int loadCallCount;

    /// <summary>How many times Godot has called <see cref="_Load"/> since the assembly loaded.</summary>
    internal static int LoadCallCount => Volatile.Read(ref loadCallCount);

    /// <inheritdoc/>
    public override string[] _GetRecognizedExtensions() => [SceneExtension, ResourceExtension];

    /// <inheritdoc/>
    public override bool _HandlesType(StringName type) => type == "PackedScene" || type == "Resource";

    /// <inheritdoc/>
    public override string _GetResourceType(string path) =>
        path.GetExtension().ToLowerInvariant() switch
        {
            SceneExtension => "PackedScene",
            ResourceExtension => "Resource",
            _ => "",
        };

    /// <inheritdoc/>
    public override Variant _Load(string path, string originalPath, bool useSubThreads, int cacheMode)
    {
        Interlocked.Increment(ref loadCallCount);
        string text = FileAccess.GetFileAsString(path);
        Error openError = FileAccess.GetOpenError();
        if (openError != Error.Ok)
        {
            GD.PushError($"{path}: godot-xaml cannot read the file: {openError}.");
            return (int)openError;
        }

        XscnDocumentKind kind = _GetResourceType(path) == "PackedScene" ? XscnDocumentKind.Scene : XscnDocumentKind.Resource;
        XscnParseResult result = XscnParser.Parse(text, kind, XscnMetadata.Value);
        if (result.HasErrors)
        {
            var lines = new LineMap(text);
            foreach (XscnDiagnostic diagnostic in result.Diagnostics)
            {
                LinePosition position = lines.OneBased(diagnostic.Span.Start);
                GD.PushError($"{path}:{position.Line}:{position.Column}: {diagnostic.Code}: {diagnostic.Message}");
            }

            return (int)Error.ParseError;
        }

        GD.PushError($"{path}: godot-xaml cannot load scenes yet; the .xscn to .tscn conversion is not built.");
        return (int)Error.Unavailable;
    }
}
