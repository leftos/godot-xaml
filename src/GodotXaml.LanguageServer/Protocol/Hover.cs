namespace GodotXaml.LanguageServer.Protocol;

/// <summary>The <c>textDocument/hover</c> request's parameters.</summary>
/// <param name="TextDocument">The document.</param>
/// <param name="Position">The 0-based position hovered.</param>
internal sealed record HoverParams(TextDocumentIdentifier TextDocument, LspPosition Position);

/// <summary>A hover's answer: what the hovered name is, and the range it covers.</summary>
/// <param name="Contents">The text shown.</param>
/// <param name="Range">The hovered name or value.</param>
internal sealed record Hover(MarkupContent Contents, LspRange Range);

/// <summary>A text in a markup the client renders.</summary>
/// <param name="Kind">The markup: <c>markdown</c> or <c>plaintext</c>.</param>
/// <param name="Value">The text.</param>
internal sealed record MarkupContent(string Kind, string Value);
