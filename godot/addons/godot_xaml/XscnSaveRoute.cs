using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Godot;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;
using FileAccess = Godot.FileAccess;

namespace GodotXaml.Addon;

/// <summary>
/// Saves a resource as <c>.xscn</c> or <c>.xres</c>: Godot's own text saver writes it to a uniquely named <c>.tscn</c> or
/// <c>.tres</c> in the OS temp folder, outside the project, so no uid is minted for it; the text is converted forward,
/// keeping the <c>x:Resources</c> keys of the file it replaces, written to a hidden staging file beside the target, and
/// renamed over the target.
/// </summary>
internal static class XscnSaveRoute
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "godot-xaml");

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Saves the resource to the path in the canonical layout.</summary>
    /// <param name="resource">The resource; a <see cref="PackedScene"/> for an <c>.xscn</c>.</param>
    /// <param name="path">The <c>.xscn</c> or <c>.xres</c> to write.</param>
    /// <param name="flags">The <see cref="ResourceSaver.SaverFlags"/> the caller passed, handed on to Godot's text saver.</param>
    /// <returns><see cref="Error.Ok"/>, or the error that stopped the save; nothing is written on an error.</returns>
    public static Error Save(Resource resource, string path, uint flags)
    {
        bool isScene = resource is PackedScene;
        Error textError = SaveAsText(resource, path, flags, isScene, out string tscnText);
        if (textError != Error.Ok)
        {
            return textError;
        }

        TscnDocument tscn;
        try
        {
            tscn = TscnReader.Read(tscnText);
        }
        catch (TscnParseException exception)
        {
            GD.PushError($"{path}: godot-xaml cannot read the text Godot's text saver wrote: {exception.Message}");
            return Error.InvalidData;
        }

        var options = new ConversionOptions
        {
            Seed = 0,
            ScriptClassOf = GodotProjectLookups.ScriptClassOf,
            Previous = Previous(path, isScene ? XscnDocumentKind.Scene : XscnDocumentKind.Resource),
        };
        ConversionResult<XscnDocument> converted = TscnToXscn.Convert(tscn, XscnMetadata.Value, options);
        if (PushIssues(path, converted.Issues))
        {
            return Error.InvalidData;
        }

        string text;
        try
        {
            text = XscnWriter.Write(converted.Document, XscnMetadata.Value);
        }
        catch (ArgumentException exception)
        {
            GD.PushError($"{path}: godot-xaml cannot write the converted document: {exception.Message}");
            return Error.InvalidData;
        }

        return WriteAtomically(path, text);
    }

    // Godot's text saver writes the resource to a temp file; its text comes back and the file is deleted.
    private static Error SaveAsText(Resource resource, string path, uint flags, bool isScene, out string text)
    {
        text = "";
        string tempPath = Path.Combine(Folder, $"{Guid.NewGuid():N}.{(isScene ? "tscn" : "tres")}").Replace('\\', '/');
        // Relative paths would be computed against the temp file, which shares no folder with the project, so they would
        // come out absolute.
        ResourceSaver.SaverFlags textFlags = (ResourceSaver.SaverFlags)flags & ~ResourceSaver.SaverFlags.RelativePaths;
        try
        {
            Directory.CreateDirectory(Folder);
            Error error = ResourceSaver.Save(resource, tempPath, textFlags);
            if (error != Error.Ok)
            {
                GD.PushError($"{path}: godot-xaml: Godot's text saver failed: {error}.");
                return error;
            }

            text = File.ReadAllText(tempPath);
            return Error.Ok;
        }
        finally
        {
            DeleteOrWarn(path, tempPath);
        }
    }

    // The file being replaced, parsed, so the conversion keeps its keys; null when there is none or it does not parse.
    private static XscnDocument? Previous(string path, XscnDocumentKind kind)
    {
        if (!FileAccess.FileExists(path))
        {
            return null;
        }

        XscnParseResult result = XscnParser.Parse(FileAccess.GetFileAsString(path), kind, XscnMetadata.Value);
        if (result.HasErrors)
        {
            GD.PushWarning($"{path}: godot-xaml: the file being replaced does not parse; its x:Resources keys and comments are not kept.");
            return null;
        }

        return result.Document;
    }

    /// <summary>
    /// Writes the text to a hidden staging file beside the target, on its volume and wherever the target is writable, then
    /// replaces the target in one rename, so a failed write never leaves a half-written file at the path.
    /// </summary>
    /// <param name="path">The <c>res://</c> file to write.</param>
    /// <param name="text">Its new text, written as UTF-8 without a byte order mark.</param>
    /// <returns><see cref="Error.Ok"/>, or <see cref="Error.FileCantWrite"/> with the target left as it was.</returns>
    internal static Error WriteAtomically(string path, string text)
    {
        string target = ProjectSettings.GlobalizePath(path);
        string stagingPath = Path.Combine(Path.GetDirectoryName(target) ?? "", $".{Guid.NewGuid():N}.{path.GetExtension()}.tmp");
        try
        {
            File.WriteAllText(stagingPath, text, Utf8NoBom);
            File.Move(stagingPath, target, overwrite: true);
            return Error.Ok;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"{path}: godot-xaml cannot write the file: {exception.Message}");
            DeleteOrWarn(path, stagingPath);
            return Error.FileCantWrite;
        }
    }

    // A cleanup that fails is a warning: it must not replace the save's own result.
    private static void DeleteOrWarn(string path, string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"{path}: godot-xaml cannot delete the temporary file {file}: {exception.Message}");
        }
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
}
