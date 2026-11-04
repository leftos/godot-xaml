using System.IO.Pipelines;
using System.Text.Json;
using System.Threading.Channels;
using GodotXaml.LanguageServer.Protocol;
using StreamJsonRpc;

namespace GodotXaml.LanguageServer.Tests;

/// <summary>
/// A language client over in-memory pipes, talking to
/// <see cref="LanguageServerHost.Run(Stream, Stream, IDebounceScheduler, TextWriter, ServerSeams)"/> through the same
/// framing and formatter. Every wait is on a channel the client fills or on the server's run; the
/// timeout is only a hang guard.
/// </summary>
internal sealed class ServerHarness : IAsyncDisposable
{
    private readonly CancellationTokenSource hang = new(TimeSpan.FromSeconds(30));
    private readonly JsonRpc client;
    private readonly ClientTarget target = new();
    private readonly Task drained;

    /// <summary>Starts a server and a client.</summary>
    /// <param name="seams">The server's metadata loader and parser; the defaults when null.</param>
    /// <param name="inlineDelays">Whether a delay's cancellation runs the server's continuation inline, inside <c>Cancel()</c>.</param>
    public ServerHarness(ServerSeams? seams = null, bool inlineDelays = false)
    {
        Scheduler = new ManualScheduler(inlineDelays);
        Pipe toServer = new();
        Pipe toClient = new();
        Stream serverInput = toServer.Reader.AsStream();
        Stream serverOutput = toClient.Writer.AsStream();
        ServerSeams used = seams ?? ServerSeams.Default;
        ServerRun = Task.Run(() => LanguageServerHost.Run(serverInput, serverOutput, Scheduler, TextWriter.Null, used));
        client = new JsonRpc(
            new HeaderDelimitedMessageHandler(toServer.Writer.AsStream(), toClient.Reader.AsStream(), LanguageServerHost.CreateFormatter())
        );
        client.AddLocalRpcTarget(target, new JsonRpcTargetOptions { UseSingleObjectParameterDeserialization = true });
        client.StartListening();
        drained = CompleteWhenDisconnectedAsync();
    }

    /// <summary>The debounce scheduler the server waits on; the test releases its delays.</summary>
    public ManualScheduler Scheduler { get; }

    /// <summary>The server's run, which ends with its exit code.</summary>
    public Task<int> ServerRun { get; }

    public ChannelReader<PublishDiagnosticsParams> Published => target.Published.Reader;

    public ChannelReader<LogMessageParams> Logged => target.Logged.Reader;

    public ChannelReader<ShowMessageParams> Shown => target.Shown.Reader;

    public CancellationToken HangGuard => hang.Token;

    public Task<JsonElement> InitializeRawAsync(InitializeParams? parameters = null) =>
        Guard(client.InvokeWithParameterObjectAsync<JsonElement>("initialize", parameters ?? new InitializeParams(), hang.Token), "initialize");

    /// <summary>Sends a request with positional arguments exactly as given, nulls included.</summary>
    public Task<JsonElement> RequestAsync(string method, params object?[] arguments) =>
        Guard(client.InvokeWithCancellationAsync<JsonElement>(method, arguments, hang.Token), method);

    public async Task<InitializeResult> InitializeAsync(InitializeParams? parameters = null)
    {
        InitializeResult result = await Guard(
            client.InvokeWithParameterObjectAsync<InitializeResult>("initialize", parameters ?? new InitializeParams(), hang.Token),
            "initialize"
        );
        await client.NotifyWithParameterObjectAsync("initialized", new InitializedParams());
        return result;
    }

    public Task ShutdownAsync() => Guard(client.InvokeWithCancellationAsync("shutdown", [], hang.Token), "shutdown");

    /// <summary>Sends <c>exit</c> and waits for the server's run to end.</summary>
    public async Task<int> ExitAsync()
    {
        await client.NotifyAsync("exit");
        return await Guard(ServerRun, "the server to exit");
    }

    /// <summary>Shuts the server down, exits it and waits until every message it sent has been read.</summary>
    public async Task<int> ShutdownAndDrainAsync()
    {
        await ShutdownAsync();
        int code = await ExitAsync();
        await Guard(drained, "the client to drain");
        return code;
    }

    public Task OpenAsync(string uri, int version, string text) =>
        client.NotifyWithParameterObjectAsync(
            "textDocument/didOpen",
            new DidOpenTextDocumentParams(new TextDocumentItem(uri, "xscn", version, text))
        );

    public Task ChangeAsync(string uri, int version, string text) =>
        client.NotifyWithParameterObjectAsync(
            "textDocument/didChange",
            new DidChangeTextDocumentParams(new VersionedTextDocumentIdentifier(uri, version), [new TextDocumentContentChangeEvent(text)])
        );

    public Task CloseAsync(string uri) =>
        client.NotifyWithParameterObjectAsync("textDocument/didClose", new DidCloseTextDocumentParams(new TextDocumentIdentifier(uri)));

    /// <summary>The next message on a channel, failing as hung when none comes.</summary>
    public Task<T> NextAsync<T>(ChannelReader<T> reader, string what) => Guard(reader.ReadAsync(hang.Token).AsTask(), what);

    /// <summary>What a channel still holds once the server has exited and the client has drained.</summary>
    public static List<T> Remaining<T>(ChannelReader<T> reader)
    {
        List<T> items = [];
        while (reader.TryRead(out T? item))
        {
            items.Add(item);
        }
        return items;
    }

    public async Task<T> Guard<T>(Task<T> task, string what)
    {
        await Guard((Task)task, what);
        return await task;
    }

    public async Task Guard(Task task, string what)
    {
        try
        {
            await task.WaitAsync(hang.Token);
        }
        catch (OperationCanceledException) when (hang.IsCancellationRequested)
        {
            Assert.Fail($"hung waiting for {what}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await Guard(ServerRun, "the server to end");
        hang.Dispose();
    }

    private async Task CompleteWhenDisconnectedAsync()
    {
        await Task.WhenAny(client.Completion);
        await Task.WhenAny(client.DispatchCompletion);
        target.Complete();
    }

    private sealed class ClientTarget
    {
        public Channel<PublishDiagnosticsParams> Published { get; } = Channel.CreateUnbounded<PublishDiagnosticsParams>();

        public Channel<LogMessageParams> Logged { get; } = Channel.CreateUnbounded<LogMessageParams>();

        public Channel<ShowMessageParams> Shown { get; } = Channel.CreateUnbounded<ShowMessageParams>();

        [JsonRpcMethod("textDocument/publishDiagnostics")]
        public void PublishDiagnostics(PublishDiagnosticsParams parameters) => Published.Writer.TryWrite(parameters);

        [JsonRpcMethod("window/logMessage")]
        public void LogMessage(LogMessageParams parameters) => Logged.Writer.TryWrite(parameters);

        [JsonRpcMethod("window/showMessage")]
        public void ShowMessage(ShowMessageParams parameters) => Shown.Writer.TryWrite(parameters);

        public void Complete()
        {
            Published.Writer.TryComplete();
            Logged.Writer.TryComplete();
            Shown.Writer.TryComplete();
        }
    }
}

/// <summary>A debounce scheduler whose every delay waits until the test releases it.</summary>
/// <param name="inline">Whether a delay completes its waiter inline, so a cancellation runs the server's continuation inside <c>Cancel()</c>.</param>
internal sealed class ManualScheduler(bool inline = false) : IDebounceScheduler
{
    private readonly Channel<PendingDelay> requested = Channel.CreateUnbounded<PendingDelay>();

    /// <summary>Each delay the server asked for, in the order it asked.</summary>
    public ChannelReader<PendingDelay> Requested => requested.Reader;

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        PendingDelay pending = new(delay, cancellationToken, inline);
        requested.Writer.TryWrite(pending);
        return pending.Task;
    }
}

/// <summary>One delay the server waits on.</summary>
internal sealed class PendingDelay
{
    private readonly TaskCompletionSource source;

    public PendingDelay(TimeSpan delay, CancellationToken cancellationToken, bool inline = false)
    {
        source = new(inline ? TaskCreationOptions.None : TaskCreationOptions.RunContinuationsAsynchronously);
        Delay = delay;
        _ = cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
    }

    public TimeSpan Delay { get; }

    public Task Task => source.Task;

    /// <summary>Says the delay elapsed now.</summary>
    public void Release() => source.TrySetResult();
}

/// <summary>File URIs in the form VS Code sends them.</summary>
internal static class VsCode
{
    /// <summary>
    /// A Windows path as VS Code writes its URI: the drive letter lowercase, its colon encoded, every segment escaped
    /// (<c>file:///c%3A/Users/x/My%20Proj/Scene.xscn</c>).
    /// </summary>
    public static string FileUri(string windowsFullPath) =>
        "file:///"
        + char.ToLowerInvariant(windowsFullPath[0])
        + "%3A"
        + string.Join('/', windowsFullPath[2..].Split('\\').Select(Uri.EscapeDataString));
}
