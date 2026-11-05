using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer;

/// <summary>
/// Formats an open document through the core writer: one edit replacing the whole buffer with the canonical text, or no
/// edit when the buffer is canonical already. A document with an error, or one the writer refuses, gets no edit and a
/// message instead.
/// </summary>
internal sealed class FormattingHandler(DocumentStore store, ProjectCache projects, IClientMessages client, ParseCache cache)
{
    /// <summary>Formats a document's buffer.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    /// <returns>The edits; an empty list for a document not open, a canonical buffer, an error or a refusal.</returns>
    public async Task<TextEdit[]> FormatAsync(string uri, CancellationToken cancellation)
    {
        if (store.Find(uri) is not { } document)
        {
            return [];
        }

        // The project's lookup takes its lock and may load metadata: it must not run on the dispatch thread.
        await Task.Yield();

        ProjectContext? project = await projects.ResolveAsync(document.Path).ConfigureAwait(false);
        if (project is null)
        {
            return [];
        }

        Written written = await Task.Run(() => Write(document, project.Metadata, cache), cancellation).ConfigureAwait(false);

        // The edit replaces the whole buffer: a change or a close that landed while it ran makes the reply stale, and
        // applying it would wipe what the user typed after asking.
        return store.IsCurrent(document) && !cancellation.IsCancellationRequested
            ? await AnswerAsync(document, written, cancellation).ConfigureAwait(false)
            : [];
    }

    // The message a failed write shows, or the one edit a successful one is.
    private async Task<TextEdit[]> AnswerAsync(OpenDocument document, Written written, CancellationToken cancellation)
    {
        if (written.HasErrors)
        {
            await ShowAsync(MessageType.Warning, $"{NameOf(document)} has errors; fix them before formatting.", cancellation).ConfigureAwait(false);
            return [];
        }
        if (written.Refusal is { } refusal)
        {
            await ShowAsync(MessageType.Error, $"{NameOf(document)} could not be formatted: {refusal}", cancellation).ConfigureAwait(false);
            return [];
        }
        if (written.Text is not { } canonical || string.Equals(canonical, document.Text, StringComparison.Ordinal))
        {
            return [];
        }

        LinePosition end = new LineMap(document.Text).ZeroBased(document.Text.Length);
        return [new TextEdit(new LspRange(new LspPosition(0, 0), new LspPosition(end.Line, end.Column)), canonical)];
    }

    // A message a cancelled request no longer wants.
    private Task ShowAsync(MessageType type, string message, CancellationToken cancellation) =>
        cancellation.IsCancellationRequested ? Task.CompletedTask : client.ShowAsync(type, message);

    // The canonical text, or which of an error and a refusal stopped it.
    private static Written Write(OpenDocument document, GodotMetadata metadata, ParseCache cache)
    {
        XscnParseResult result = cache.Get(document, metadata);
        if (result.HasErrors)
        {
            return new Written(true, null, null);
        }

        try
        {
            return new Written(false, XscnWriter.Write(result.Document, metadata), null);
        }
        catch (Exception exception) when (WriterRefusal.Is(exception))
        {
            return new Written(false, null, exception.Message);
        }
    }

    // The document's file name, or the last, unescaped segment of its URI for a document not on disk.
    private static string NameOf(OpenDocument document) =>
        document.Path is null ? Uri.UnescapeDataString(document.Uri[(document.Uri.LastIndexOf('/') + 1)..]) : Path.GetFileName(document.Path);

    // A document the writer was asked to write: its text, or that it had an error or was refused.
    private sealed record Written(bool HasErrors, string? Text, string? Refusal);
}
