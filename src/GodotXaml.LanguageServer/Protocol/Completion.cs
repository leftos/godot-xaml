namespace GodotXaml.LanguageServer.Protocol;

/// <summary>The <c>textDocument/completion</c> request's parameters; the client's trigger context is not read.</summary>
/// <param name="TextDocument">The document.</param>
/// <param name="Position">The 0-based cursor position, between two characters.</param>
internal sealed record CompletionParams(TextDocumentIdentifier TextDocument, LspPosition Position);

/// <summary>What the server completes on, advertised in <c>initialize</c>.</summary>
/// <param name="TriggerCharacters">The characters that ask for completions as they are typed.</param>
internal sealed record CompletionOptions(string[] TriggerCharacters);

/// <summary>A completion's answer: every item, filtered by the server.</summary>
/// <param name="IsIncomplete">Whether typing more should ask again; false, the list is whole.</param>
/// <param name="Items">The items, sorted by label.</param>
internal sealed record CompletionList(bool IsIncomplete, CompletionItem[] Items);

/// <summary>One completion.</summary>
/// <param name="Label">The name it writes.</param>
/// <param name="Kind">What the name is.</param>
/// <param name="Detail">A type, <c>signal</c> or a parent class shown beside the label, or null.</param>
/// <param name="TextEdit">The edit that writes the label over the token at the cursor.</param>
internal sealed record CompletionItem(string Label, CompletionItemKind Kind, string? Detail, TextEdit TextEdit);

/// <summary>The kinds of completion item the server sends, with LSP's numbers.</summary>
internal enum CompletionItemKind
{
    /// <summary>A Godot class.</summary>
    Class = 7,

    /// <summary>A property, a theme override or a property element.</summary>
    Property = 10,

    /// <summary>A name of the format: an <c>x:</c> element or attribute, <c>Name</c>, <c>Script</c>.</summary>
    Keyword = 14,

    /// <summary>A signal.</summary>
    Event = 23,
}
