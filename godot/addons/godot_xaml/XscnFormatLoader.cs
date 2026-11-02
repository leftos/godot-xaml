using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Xml;
using Godot;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Tscn;
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

    private static readonly ConversionOptions Options = new()
    {
        Seed = 0,
        ExtResourceTypes = GodotProjectLookups.Instance,
        ScriptClassOf = GodotProjectLookups.ScriptClassOf,
        Uid = null,
    };

    private static int loadCallCount;

    /// <summary>How many times Godot has called <see cref="_Load"/> since the assembly loaded.</summary>
    internal static int LoadCallCount => Volatile.Read(ref loadCallCount);

    /// <inheritdoc/>
    public override string[] _GetRecognizedExtensions() => [SceneExtension, ResourceExtension];

    /// <inheritdoc/>
    public override bool _HandlesType(StringName type) => ClassDB.IsParentClass(type, "Resource");

    /// <inheritdoc/>
    public override string _GetResourceType(string path) =>
        path.GetExtension().ToLowerInvariant() switch
        {
            SceneExtension => "PackedScene",
            ResourceExtension => XresRootClass(path),
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

        bool isScene = path.GetExtension().Equals(SceneExtension, StringComparison.OrdinalIgnoreCase);
        XscnParseResult result = XscnParser.Parse(text, isScene ? XscnDocumentKind.Scene : XscnDocumentKind.Resource, XscnMetadata.Value);
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

        ConversionResult<TscnDocument> converted = XscnToTscn.Convert(result.Document, XscnMetadata.Value, Options);
        if (PushIssues(path, converted.Issues))
        {
            return (int)Error.ParseError;
        }

        string typeHint = isScene ? "PackedScene" : converted.Document.Header.Type ?? "Resource";
        bool deep = cacheMode is (int)CacheMode.IgnoreDeep or (int)CacheMode.ReplaceDeep;
        return XscnLoadRoute.Load(path, TscnWriter.Write(converted.Document), isScene, typeHint, deep);
    }

    /// <summary>Pushes each conversion issue to Godot's log, and tells whether any was an error.</summary>
    private static bool PushIssues(string path, ImmutableArray<ConversionIssue> issues)
    {
        bool anyError = false;
        foreach (ConversionIssue issue in issues)
        {
            string message = $"{path}: {issue.Code}: {issue.Message} ({issue.Location})";
            if (issue.Severity == ConversionSeverity.Error)
            {
                anyError = true;
                GD.PushError(message);
            }
            else
            {
                GD.PushWarning(message);
            }
        }

        return anyError;
    }

    /// <summary>The class an <c>.xres</c> file's root tag names, read without parsing the rest; <c>Resource</c> when it names none.</summary>
    private static string XresRootClass(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true };
        try
        {
            using FileStream stream = File.OpenRead(ProjectSettings.GlobalizePath(path));
            using var reader = XmlReader.Create(stream, settings);
            bool named =
                reader.MoveToContent() == XmlNodeType.Element
                && reader.Prefix.Length == 0
                && ClassDB.ClassExists(reader.LocalName)
                && ClassDB.IsParentClass(reader.LocalName, "Resource");
            return named ? reader.LocalName : "Resource";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return "Resource";
        }
    }
}
