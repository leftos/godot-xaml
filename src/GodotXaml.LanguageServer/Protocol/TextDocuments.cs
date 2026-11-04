namespace GodotXaml.LanguageServer.Protocol;

/// <summary>A document the client opened, with its whole text.</summary>
/// <param name="Uri">The document's URI.</param>
/// <param name="LanguageId">The client's language id.</param>
/// <param name="Version">The client's version of the text.</param>
/// <param name="Text">The text.</param>
internal sealed record TextDocumentItem(string Uri, string LanguageId, int Version, string Text);

/// <summary>A document by its URI.</summary>
/// <param name="Uri">The document's URI.</param>
internal sealed record TextDocumentIdentifier(string Uri);

/// <summary>A document by its URI and the version a change makes.</summary>
/// <param name="Uri">The document's URI.</param>
/// <param name="Version">The version after the change.</param>
internal sealed record VersionedTextDocumentIdentifier(string Uri, int Version);

/// <summary>One change of a <c>didChange</c>; under full sync, the whole new text.</summary>
/// <param name="Text">The new text.</param>
internal sealed record TextDocumentContentChangeEvent(string Text);

/// <summary>The <c>textDocument/didOpen</c> notification's parameters.</summary>
/// <param name="TextDocument">The opened document.</param>
internal sealed record DidOpenTextDocumentParams(TextDocumentItem TextDocument);

/// <summary>The <c>textDocument/didChange</c> notification's parameters.</summary>
/// <param name="TextDocument">The document and its new version.</param>
/// <param name="ContentChanges">The changes; under full sync the last one holds the whole text.</param>
internal sealed record DidChangeTextDocumentParams(VersionedTextDocumentIdentifier TextDocument, TextDocumentContentChangeEvent[] ContentChanges);

/// <summary>The <c>textDocument/didClose</c> notification's parameters.</summary>
/// <param name="TextDocument">The closed document.</param>
internal sealed record DidCloseTextDocumentParams(TextDocumentIdentifier TextDocument);
