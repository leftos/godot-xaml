using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;
using Microsoft.VisualStudio.Threading;
using PolyType;
using StreamJsonRpc;

namespace GodotXaml.LanguageServer;

/// <summary>Runs the language server over a pair of streams: LSP's <c>Content-Length</c> framing, JSON-RPC, and the server.</summary>
internal static class LanguageServerHost
{
    /// <summary>The server's name, in <c>serverInfo</c> and as every diagnostic's <c>source</c>.</summary>
    public const string ServerName = "godot-xaml";

    /// <summary>The server's version: the assembly's informational version.</summary>
    public static string Version { get; } =
        typeof(LanguageServerHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>Serves one client until it sends <c>exit</c> or closes the input.</summary>
    /// <param name="input">What the client sends.</param>
    /// <param name="output">Where the server answers; nothing else writes to it.</param>
    /// <param name="scheduler">Waits out the debounce of a change.</param>
    /// <param name="stderr">Where the logs go besides the client's log.</param>
    /// <returns>The process's exit code: 0 after <c>shutdown</c> then <c>exit</c>, else 1.</returns>
    public static Task<int> Run(Stream input, Stream output, IDebounceScheduler scheduler, TextWriter stderr) =>
        Run(input, output, scheduler, stderr, ServerSeams.Default);

    /// <summary>Serves one client with the seams a test replaces.</summary>
    /// <param name="input">What the client sends.</param>
    /// <param name="output">Where the server answers.</param>
    /// <param name="scheduler">Waits out the debounce of a change.</param>
    /// <param name="stderr">Where the logs go besides the client's log.</param>
    /// <param name="seams">The metadata loader and the parser.</param>
    /// <returns>The process's exit code.</returns>
    public static async Task<int> Run(Stream input, Stream output, IDebounceScheduler scheduler, TextWriter stderr, ServerSeams seams)
    {
        var log = TextWriter.Synchronized(stderr);
        using JsonRpc rpc = new(new HeaderDelimitedMessageHandler(output, input, CreateFormatter()));
        ClientChannel client = new(rpc, log);
        DocumentStore store = new(log);
        ProjectCache projects = new(seams.LoadMetadata, client);
        ParseCache cache = new(store, seams.Parse);
        using DiagnosticsPublisher publisher = new(store, projects, client, scheduler, cache);
        LanguageServerTarget server = new(
            client,
            publisher,
            new FormattingHandler(store, projects, client, cache),
            new HoverHandler(store, projects, cache, seams.LoadDocs),
            new CompletionHandler(store, projects, cache)
        );

        // The transport's own warnings and errors (a message it could not read or answer) go to stderr.
        rpc.TraceSource = new TraceSource("godot-xaml.jsonrpc", SourceLevels.Warning);
        rpc.TraceSource.Listeners.Clear();
        rpc.TraceSource.Listeners.Add(new TextWriterTraceListener(log));

        // LSP's notifications are ordered: a change must not overtake the open or the change before it.
        rpc.SynchronizationContext = new NonConcurrentSynchronizationContext(sticky: false);
        rpc.AddLocalRpcTarget(
            RpcTargetMetadata.FromShape<ILanguageServerTarget>(),
            server,
            new JsonRpcTargetOptions { UseSingleObjectParameterDeserialization = true }
        );
        rpc.StartListening();
        await Task.WhenAny(rpc.Completion, server.Exited);

        // An exit without a shutdown still lets the running parses end before the connection goes.
        await publisher.StopAsync();
        return server.ExitCode;
    }

    /// <summary>The formatter both ends use: the source-generated LSP types, camelCase, no explicit nulls.</summary>
    /// <returns>A new formatter.</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The resolver is LspJsonContext, as StreamJsonRpc's NativeAOT guide documents; it holds every type the server "
            + "reads or writes: the LSP messages, CommonErrorData and RequestId. A type missing from it fails at run time, not here."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "The resolver is LspJsonContext, as StreamJsonRpc's NativeAOT guide documents; it holds every type the server "
            + "reads or writes: the LSP messages, CommonErrorData and RequestId. A type missing from it fails at run time, not here."
    )]
    public static SystemTextJsonFormatter CreateFormatter() =>
        new()
        {
            JsonSerializerOptions = new JsonSerializerOptions
            {
                TypeInfoResolver = LspJsonContext.Default,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            },
        };
}

/// <summary>Loads the metadata a project's Godot version names.</summary>
/// <param name="projectVersion">The project's Godot minor version, or null when it names none or there is no project.</param>
/// <returns>The metadata, the version loaded, and why it is not the project's.</returns>
internal delegate LoadedMetadata MetadataLoader(string? projectVersion);

/// <summary>Parses a document's text.</summary>
/// <param name="text">The text.</param>
/// <param name="kind">Scene or resource.</param>
/// <param name="metadata">The metadata it is read with.</param>
/// <returns>The parse.</returns>
internal delegate XscnParseResult ParseFunction(string text, XscnDocumentKind kind, GodotMetadata metadata);

/// <summary>A loaded metadata and the version it is.</summary>
/// <param name="Metadata">The metadata.</param>
/// <param name="Version">The Godot minor version loaded.</param>
/// <param name="Warning">Why it is not the project's version, or null.</param>
internal sealed record LoadedMetadata(GodotMetadata Metadata, string Version, string? Warning);

/// <summary>What a test replaces in the server: the metadata loader, the parser and the class reference loader.</summary>
/// <param name="LoadMetadata">Loads a project's metadata.</param>
/// <param name="Parse">Parses a document.</param>
/// <param name="LoadDocs">Loads a Godot minor version's class reference.</param>
internal sealed record ServerSeams(MetadataLoader LoadMetadata, ParseFunction Parse, DocsLoader LoadDocs)
{
    /// <summary>The bundled metadata, <see cref="XscnParser.Parse"/> and the bundled class reference.</summary>
    public static ServerSeams Default { get; } = new(LoadBundled, XscnParser.Parse, ClassDocs.LoadBundled);

    private static LoadedMetadata LoadBundled(string? projectVersion)
    {
        var metadata = GodotMetadata.LoadBundledForProject(projectVersion, out string version, out string? warning);
        return new LoadedMetadata(metadata, version, warning);
    }
}

/// <summary>What the server sends the client besides answers: logs, messages and diagnostics.</summary>
internal interface IClientMessages
{
    /// <summary>Writes a line to the client's log and to stderr.</summary>
    /// <param name="type">How it is meant.</param>
    /// <param name="message">The line.</param>
    /// <returns>The send.</returns>
    Task LogAsync(MessageType type, string message);

    /// <summary>Shows the user a message, and writes it to stderr.</summary>
    /// <param name="type">How it is meant.</param>
    /// <param name="message">The message.</param>
    /// <returns>The send.</returns>
    Task ShowAsync(MessageType type, string message);

    /// <summary>Publishes a document's diagnostics.</summary>
    /// <param name="parameters">The document and its diagnostics.</param>
    /// <returns>The send.</returns>
    Task PublishAsync(PublishDiagnosticsParams parameters);
}

/// <summary>Sends to the client over JSON-RPC; a send after the connection closed is dropped.</summary>
internal sealed class ClientChannel(JsonRpc rpc, TextWriter stderr) : IClientMessages
{
    /// <inheritdoc/>
    public Task LogAsync(MessageType type, string message)
    {
        stderr.WriteLine($"{type}: {message}");
        return SendAsync("window/logMessage", new LogMessageParams(type, message));
    }

    /// <inheritdoc/>
    public Task ShowAsync(MessageType type, string message)
    {
        stderr.WriteLine($"{type}: {message}");
        return SendAsync("window/showMessage", new ShowMessageParams(type, message));
    }

    /// <inheritdoc/>
    public Task PublishAsync(PublishDiagnosticsParams parameters) => SendAsync("textDocument/publishDiagnostics", parameters);

    // NamedArgs would write the C# property names as the keys; SystemTextJsonFormatter writes a plain object whole,
    // through the source-generated LspJsonContext that holds every type sent here, so nothing is reflected over.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "SystemTextJsonFormatter writes a parameter object that is not NamedArgs whole, through LspJsonContext, "
            + "which holds every type sent here; nothing reflects over its members."
    )]
    private async Task SendAsync(string method, object parameters)
    {
        try
        {
            await rpc.NotifyWithParameterObjectAsync(method, parameters);
        }
        catch (ObjectDisposedException exception)
        {
            // The client is gone; nothing reads a late message.
            stderr.WriteLine($"Error: {method} not sent, the connection is closed: {exception.Message}");
        }
        catch (ConnectionLostException exception)
        {
            stderr.WriteLine($"Error: {method} not sent, the connection was lost: {exception.Message}");
        }
    }
}

/// <summary>
/// The RPC methods the client calls. Its source-generated shape registers them with StreamJsonRpc
/// (<see cref="RpcTargetMetadata.FromShape{T}()"/>), so a NativeAOT build dispatches without reflecting over the target.
/// </summary>
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
internal partial interface ILanguageServerTarget
{
    /// <summary>Answers <c>initialize</c> with the capabilities.</summary>
    /// <param name="parameters">The client's parameters, unread.</param>
    /// <returns>The capabilities and the server's name and version.</returns>
    [JsonRpcMethod("initialize")]
    InitializeResult Initialize(InitializeParams parameters);

    /// <summary>Logs that the server started.</summary>
    /// <returns>The log's send.</returns>
    [JsonRpcMethod("initialized")]
    Task InitializedAsync();

    /// <summary>Stops all work; after it, every request is refused and only <c>exit</c> is heard.</summary>
    /// <returns>The shutdown.</returns>
    [JsonRpcMethod("shutdown")]
    Task ShutdownAsync();

    /// <summary>Ends the run, with 0 when <c>shutdown</c> came first and 1 when it did not.</summary>
    [JsonRpcMethod("exit")]
    void Exit();

    /// <summary>Accepts <c>$/setTrace</c>; the server keeps no trace level.</summary>
    /// <param name="parameters">The level, unread.</param>
    [JsonRpcMethod("$/setTrace")]
    void SetTrace(JsonElement parameters);

    /// <summary>Accepts <c>workspace/didChangeConfiguration</c>; the server reads no settings yet.</summary>
    /// <param name="parameters">The settings, unread.</param>
    [JsonRpcMethod("workspace/didChangeConfiguration")]
    void DidChangeConfiguration(JsonElement parameters);

    /// <summary>Opens a buffer and publishes its diagnostics at once.</summary>
    /// <param name="parameters">The document and its text.</param>
    [JsonRpcMethod("textDocument/didOpen")]
    void DidOpen(DidOpenTextDocumentParams parameters);

    /// <summary>Replaces a buffer's text with the last change's; the diagnostics follow after the debounce.</summary>
    /// <param name="parameters">The document, its version and the changes.</param>
    [JsonRpcMethod("textDocument/didChange")]
    void DidChange(DidChangeTextDocumentParams parameters);

    /// <summary>Drops a buffer and clears its diagnostics.</summary>
    /// <param name="parameters">The document.</param>
    [JsonRpcMethod("textDocument/didClose")]
    void DidClose(DidCloseTextDocumentParams parameters);

    /// <summary>Formats a document: one edit replacing its whole text, or none.</summary>
    /// <param name="parameters">The document and the client's formatting options, unread.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    /// <returns>The edits, an empty list when there is nothing to do.</returns>
    [JsonRpcMethod("textDocument/formatting")]
    Task<TextEdit[]> Formatting(DocumentFormattingParams parameters, CancellationToken cancellation);

    /// <summary>Describes the name or value at a position: a class, a property, a signal, an enum value, a key or an <c>x:</c> attribute.</summary>
    /// <param name="parameters">The document and the position.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    /// <returns>The hover, or null when there is nothing to describe there.</returns>
    [JsonRpcMethod("textDocument/hover")]
    Task<Hover?> HoverAsync(HoverParams parameters, CancellationToken cancellation);

    /// <summary>Completes the element or attribute name at a position.</summary>
    /// <param name="parameters">The document, the cursor and how the client triggered the request.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    /// <returns>The completions, or null where the server completes nothing.</returns>
    [JsonRpcMethod("textDocument/completion")]
    Task<CompletionList?> CompletionAsync(CompletionParams parameters, CancellationToken cancellation);
}

/// <summary>The RPC methods: the lifecycle and its order rules, then the document notifications and requests.</summary>
internal sealed class LanguageServerTarget(
    IClientMessages client,
    DiagnosticsPublisher publisher,
    FormattingHandler formatting,
    HoverHandler hover,
    CompletionHandler completion
) : ILanguageServerTarget
{
    // The characters that open a tag, a prefix's name, a property element's name, an attribute, a value and a flags part.
    private static readonly string[] CompletionTriggers = ["<", ":", ".", " ", "\"", ","];

    private readonly Lock gate = new();
    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool initialized;
    private bool shutDown;
    private int exitCode = 1;

    /// <summary>Completes when the client sends <c>exit</c>.</summary>
    public Task Exited => exited.Task;

    /// <summary>0 after <c>shutdown</c> then <c>exit</c>, else 1.</summary>
    public int ExitCode
    {
        get
        {
            lock (gate)
            {
                return exitCode;
            }
        }
    }

    /// <inheritdoc/>
    public InitializeResult Initialize(InitializeParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (gate)
        {
            if (shutDown || initialized)
            {
                throw Refused(LspErrorCodes.InvalidRequest, shutDown ? "The server has shut down." : "The server is already initialized.");
            }
            initialized = true;
        }

        return new InitializeResult(
            new ServerCapabilities(
                new TextDocumentSyncOptions(OpenClose: true, Change: TextDocumentSyncKind.Full),
                DocumentFormattingProvider: true,
                HoverProvider: true,
                CompletionProvider: new CompletionOptions(CompletionTriggers)
            ),
            new ServerInfo(LanguageServerHost.ServerName, LanguageServerHost.Version)
        );
    }

    /// <inheritdoc/>
    public Task InitializedAsync() =>
        client.LogAsync(MessageType.Info, $"{LanguageServerHost.ServerName} language server {LanguageServerHost.Version} started");

    /// <inheritdoc/>
    public async Task ShutdownAsync()
    {
        lock (gate)
        {
            EnsureServing();
            shutDown = true;
        }
        await client.LogAsync(MessageType.Info, $"{LanguageServerHost.ServerName} language server shutting down");
        await publisher.StopAsync();
    }

    /// <inheritdoc/>
    public void Exit()
    {
        lock (gate)
        {
            exitCode = shutDown ? 0 : 1;
        }
        exited.TrySetResult();
    }

    /// <inheritdoc/>
    public void SetTrace(JsonElement parameters) { }

    /// <inheritdoc/>
    public void DidChangeConfiguration(JsonElement parameters) { }

    /// <inheritdoc/>
    public void DidOpen(DidOpenTextDocumentParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (Serving())
        {
            TextDocumentItem document = parameters.TextDocument;
            publisher.Opened(document.Uri, document.Version, document.Text);
        }
    }

    /// <inheritdoc/>
    public void DidChange(DidChangeTextDocumentParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (Serving() && parameters.ContentChanges is [.., TextDocumentContentChangeEvent last])
        {
            publisher.Changed(parameters.TextDocument.Uri, parameters.TextDocument.Version, last.Text);
        }
    }

    /// <inheritdoc/>
    public void DidClose(DidCloseTextDocumentParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (Serving())
        {
            publisher.Closed(parameters.TextDocument.Uri);
        }
    }

    /// <inheritdoc/>
    public Task<TextEdit[]> Formatting(DocumentFormattingParams parameters, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (gate)
        {
            EnsureServing();
        }
        return formatting.FormatAsync(parameters.TextDocument.Uri, cancellation);
    }

    /// <inheritdoc/>
    public Task<Hover?> HoverAsync(HoverParams parameters, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (gate)
        {
            EnsureServing();
        }
        return hover.HoverAsync(parameters.TextDocument.Uri, parameters.Position, cancellation);
    }

    /// <inheritdoc/>
    public Task<CompletionList?> CompletionAsync(CompletionParams parameters, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (gate)
        {
            EnsureServing();
        }
        return completion.CompleteAsync(parameters.TextDocument.Uri, parameters.Position, parameters.Context, cancellation);
    }

    // A notification is dropped before initialize and after shutdown.
    private bool Serving()
    {
        lock (gate)
        {
            return initialized && !shutDown;
        }
    }

    // Called under the gate: a request is refused before initialize and after shutdown.
    private void EnsureServing()
    {
        if (shutDown)
        {
            throw Refused(LspErrorCodes.InvalidRequest, "The server has shut down.");
        }
        if (!initialized)
        {
            throw Refused(LspErrorCodes.ServerNotInitialized, "The server is not initialized.");
        }
    }

    private static LocalRpcException Refused(int code, string message) => new(message) { ErrorCode = code };
}
