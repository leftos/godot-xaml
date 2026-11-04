namespace GodotXaml.LanguageServer.Protocol;

/// <summary>The <c>textDocument/formatting</c> request's parameters.</summary>
/// <param name="TextDocument">The document to format.</param>
/// <param name="Options">How the client would format it; the server reads none of it, the writer's canonical form wins.</param>
internal sealed record DocumentFormattingParams(TextDocumentIdentifier TextDocument, FormattingOptions? Options);

/// <summary>How a client would like a document formatted; the server reads none of it.</summary>
/// <param name="TabSize">The client's tab width.</param>
/// <param name="InsertSpaces">Whether the client indents with spaces.</param>
internal sealed record FormattingOptions(int TabSize, bool InsertSpaces);

/// <summary>A replacement of a range of a document with a text.</summary>
/// <param name="Range">The range it replaces.</param>
/// <param name="NewText">The text it puts there.</param>
internal sealed record TextEdit(LspRange Range, string NewText);
