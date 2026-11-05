using System.Collections.Concurrent;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;

namespace GodotXaml.LanguageServer;

/// <summary>
/// The parse of each open document, kept while its text and its metadata stay the same: the parse a publish of the
/// document's diagnostics made answers a later hover, completion or format of the same text. A change gives the document
/// a new stamp and another project's metadata is another instance, so neither answers from the entry. A parse that
/// finishes after its document closed leaves the entry removed, and an older parse never replaces a newer one.
/// </summary>
/// <param name="store">The open documents, which say whether a parse that just finished is still the current one.</param>
/// <param name="parse">The parser a miss runs; it is the one seam a test replaces.</param>
internal sealed class ParseCache(DocumentStore store, ParseFunction parse)
{
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);

    /// <summary>A document's parse, made now when none is kept for its text and metadata.</summary>
    /// <param name="document">The open document.</param>
    /// <param name="metadata">The metadata it is read with.</param>
    /// <returns>The parse.</returns>
    public XscnParseResult Get(OpenDocument document, GodotMetadata metadata)
    {
        if (entries.TryGetValue(document.Uri, out Entry? entry) && entry.Stamp == document.Stamp && ReferenceEquals(entry.Metadata, metadata))
        {
            return entry.Result;
        }

        XscnParseResult result = parse(document.Text, document.Kind, metadata);
        Entry fresh = new(document.Stamp, metadata, result);

        // A stamp only rises, so a parse that started before a newer one finished keeps the newer entry.
        entries.AddOrUpdate(document.Uri, fresh, (_, old) => old.Stamp > fresh.Stamp ? old : fresh);

        // The write comes before the check: a close's Remove runs either before this write, which the check then undoes,
        // or after it, which removes it. The pair's key and value keep a newer entry a reopen made from being dropped.
        if (!store.IsCurrent(document))
        {
            entries.TryRemove(KeyValuePair.Create(document.Uri, fresh));
        }

        return result;
    }

    /// <summary>Forgets a document's parse.</summary>
    /// <param name="uri">The document's URI.</param>
    public void Remove(string uri) => entries.TryRemove(uri, out _);

    /// <summary>Whether a parse is kept for a document.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <returns>Whether an entry is kept.</returns>
    internal bool Contains(string uri) => entries.ContainsKey(uri);

    private sealed record Entry(long Stamp, GodotMetadata Metadata, XscnParseResult Result);
}
