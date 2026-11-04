using System.Text.Json;

namespace GodotXaml.LanguageServer.Protocol;

/// <summary>The <c>initialize</c> request's parameters; the server reads none of the client's capabilities yet.</summary>
/// <param name="ProcessId">The client's process id, or null.</param>
/// <param name="RootUri">The workspace root, or null.</param>
/// <param name="WorkspaceFolders">The workspace folders, or null.</param>
/// <param name="Capabilities">The client's capabilities, kept unread.</param>
/// <param name="InitializationOptions">The client's options, kept unread.</param>
internal sealed record InitializeParams(
    int? ProcessId = null,
    string? RootUri = null,
    WorkspaceFolder[]? WorkspaceFolders = null,
    JsonElement? Capabilities = null,
    JsonElement? InitializationOptions = null
);

/// <summary>A workspace folder the client has open.</summary>
/// <param name="Uri">The folder's URI.</param>
/// <param name="Name">The folder's name in the client.</param>
internal sealed record WorkspaceFolder(string Uri, string Name);

/// <summary>The <c>initialized</c> notification's parameters, an empty object.</summary>
internal sealed class InitializedParams;

/// <summary>The <c>initialize</c> result.</summary>
/// <param name="Capabilities">What the server does.</param>
/// <param name="ServerInfo">The server's name and version.</param>
internal sealed record InitializeResult(ServerCapabilities Capabilities, ServerInfo ServerInfo);

/// <summary>What the server does: text synchronisation, whole-document formatting and hover.</summary>
/// <param name="TextDocumentSync">How the client sends a document's text.</param>
/// <param name="DocumentFormattingProvider">Whether the server formats a whole document.</param>
/// <param name="HoverProvider">Whether the server answers a hover.</param>
internal sealed record ServerCapabilities(TextDocumentSyncOptions TextDocumentSync, bool DocumentFormattingProvider, bool HoverProvider);

/// <summary>How the client sends a document's text.</summary>
/// <param name="OpenClose">Whether the client sends <c>didOpen</c> and <c>didClose</c>.</param>
/// <param name="Change">What a <c>didChange</c> carries.</param>
internal sealed record TextDocumentSyncOptions(bool OpenClose, TextDocumentSyncKind Change);

/// <summary>What a <c>didChange</c> carries.</summary>
internal enum TextDocumentSyncKind
{
    /// <summary>No change is sent.</summary>
    None = 0,

    /// <summary>The whole text, each time.</summary>
    Full = 1,

    /// <summary>The edited ranges.</summary>
    Incremental = 2,
}

/// <summary>The server's name and version.</summary>
/// <param name="Name">The name.</param>
/// <param name="Version">The version, or null.</param>
internal sealed record ServerInfo(string Name, string? Version);

/// <summary>The JSON-RPC error codes the server answers with.</summary>
internal static class LspErrorCodes
{
    /// <summary>A request other than <c>initialize</c> came before it.</summary>
    public const int ServerNotInitialized = -32002;

    /// <summary>A request the server refuses in its state, such as one after <c>shutdown</c>.</summary>
    public const int InvalidRequest = -32600;
}
