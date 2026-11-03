namespace GodotXaml.Cli;

/// <summary>The codes a <c>convert</c> run prints for the project-wide work around the files it converts.</summary>
internal static class CliCodes
{
    /// <summary>A source with no <c>project.godot</c> above it, so its <c>res://</c> references are not rewritten.</summary>
    internal const string NoProjectRoot = "GXP0001";

    /// <summary>A reference from a file the run does not write to a file it converts.</summary>
    internal const string UntouchedReference = "GXP0002";

    /// <summary>A source that still carries the uid its output now also carries.</summary>
    internal const string SharedUid = "GXP0003";

    /// <summary>Text the rewriter could not rewrite, because it holds a converted path without being a path itself.</summary>
    internal const string UnrewrittenPath = "GXP0004";

    /// <summary>A written file that names the output of a file this run could not write.</summary>
    internal const string FailedWrite = "GXP0005";

    /// <summary>A script or <c>project.godot</c> string that holds a converted path inside longer text, or may build one at runtime.</summary>
    internal const string BuiltPath = "GXP0006";

    /// <summary>A <c>--project</c> run in which a source was not converted, so nothing in the project changed.</summary>
    internal const string OriginalsKept = "GXP0007";

    /// <summary>A script or <c>project.godot</c> of a <c>--project</c> run that is not UTF-8 text, so it is neither scanned nor edited.</summary>
    internal const string NotUtf8 = "GXP0008";

    /// <summary>A <c>.tscn</c> or <c>.tres</c> the reader cannot read; the file is not converted.</summary>
    internal const string ReadFailed = "GXT0001";

    /// <summary>A document the conversion or the writer refuses; the file is not written.</summary>
    internal const string CoreRefusal = "GXT0002";

    /// <summary>A <c>.tres</c> holding a scene or a <c>.tscn</c> holding a resource; the file is not converted.</summary>
    internal const string KindMismatch = "GXT0003";
}
