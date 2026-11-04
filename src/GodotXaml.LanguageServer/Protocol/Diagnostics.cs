namespace GodotXaml.LanguageServer.Protocol;

/// <summary>A 0-based line and a column in UTF-16 code units.</summary>
/// <param name="Line">The line.</param>
/// <param name="Character">The column.</param>
internal sealed record LspPosition(int Line, int Character);

/// <summary>A range of a document, its end exclusive.</summary>
/// <param name="Start">The first position.</param>
/// <param name="End">The position just past the range.</param>
internal sealed record LspRange(LspPosition Start, LspPosition End);

/// <summary>Where a diagnostic's code is described.</summary>
/// <param name="Href">The description's URL.</param>
internal sealed record CodeDescription(string Href);

/// <summary>How serious a diagnostic is.</summary>
internal enum DiagnosticSeverity
{
    /// <summary>An error.</summary>
    Error = 1,

    /// <summary>A warning.</summary>
    Warning = 2,

    /// <summary>Information.</summary>
    Information = 3,

    /// <summary>A hint.</summary>
    Hint = 4,
}

/// <summary>A problem the server reports in a document.</summary>
/// <param name="Range">Where the problem lies.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Code">The <c>GX</c> code.</param>
/// <param name="CodeDescription">Where the code is described.</param>
/// <param name="Source">Who reports it: <c>godot-xaml</c>.</param>
/// <param name="Message">What is wrong.</param>
internal sealed record LspDiagnostic(
    LspRange Range,
    DiagnosticSeverity Severity,
    string Code,
    CodeDescription? CodeDescription,
    string Source,
    string Message
);

/// <summary>The <c>textDocument/publishDiagnostics</c> notification's parameters.</summary>
/// <param name="Uri">The document's URI.</param>
/// <param name="Version">The version the diagnostics are for, or null.</param>
/// <param name="Diagnostics">Every diagnostic of the document; an empty list clears them.</param>
internal sealed record PublishDiagnosticsParams(string Uri, int? Version, LspDiagnostic[] Diagnostics);
