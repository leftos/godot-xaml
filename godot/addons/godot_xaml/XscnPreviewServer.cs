using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Godot;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Addon;

/// <summary>
/// The preview server: a loopback TCP listener a Visual Studio client finds through the discovery file
/// (<see cref="XscnPreviewDiscovery"/>) and asks for a document's Control rectangles over JSON-RPC 2.0 with
/// <c>Content-Length</c> framing (<see cref="XscnPreviewFraming"/>, <see cref="XscnPreviewProtocol"/>).
/// </summary>
/// <remarks>
/// <para>
/// The accept loop, each connection's reader and each connection's writer run on the thread pool, never on Godot's
/// main-thread context, and never call Godot. Every Godot call runs on the main thread, in <see cref="Pump"/>, which the
/// host calls each frame, and in <see cref="Start"/> and <see cref="Stop"/>. The main thread hands a reply to the
/// connection's outbox, whose writer sends it, so it never writes to a socket. One lock covers the stopping flag, the
/// pending renders and the connections.
/// </para>
/// <para>
/// A connection opens with <c>ping {token, project}</c> within <see cref="XscnPreviewFraming.MaxUnauthenticatedBody"/>
/// and ten seconds; any other first request is refused and the connection closed. At most one render runs at a time, and
/// at most one waits per connection and document: a newer one supersedes it, which answers
/// <see cref="XscnPreviewProtocol.Cancelled"/>.
/// </para>
/// </remarks>
/// <param name="discoveryFolder">The folder the discovery file goes in, a full path.</param>
/// <param name="project">The project's folder, which <c>ping</c> must name.</param>
/// <param name="host">The node the render SubViewports go under; it must be inside the tree.</param>
internal sealed class XscnPreviewServer(string discoveryFolder, string project, Node host) : IDisposable
{
    /// <summary>The project setting that starts the server in the editor; off when unset.</summary>
    internal const string EnabledSetting = "godot_xaml/preview_server/enabled";

    /// <summary>The largest SubViewport side; a larger request is clamped before the size is set, which allocates.</summary>
    internal const int MaxSide = 4096;

    private const int MaxAuthenticated = 8;

    private const int MaxUnauthenticated = 4;

    private const int MaxQueuedPerConnection = 16;

    // Layout settles over two process frames: Container sorting is deferred (Container::queue_sort, scene/gui/container.cpp).
    private const ulong FramesToSettle = 2;

    private static readonly TimeSpan AuthenticationDeadline = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan FlushBound = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan JoinBound = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan FirstBackoff = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan LastBackoff = TimeSpan.FromSeconds(2);

    private readonly object gate = new();

    private readonly Dictionary<(Connection Connection, string Document), PendingRender> pending = [];

    private readonly HashSet<Connection> connections = [];

    private readonly CancellationTokenSource stopSource = new();

    private TcpListener? listener;

    private Task acceptLoop = Task.CompletedTask;

    private string? discoveryFile;

    private byte[] tokenBytes = [];

    private bool started;

    private bool stopping;

    private long sequence;

    private ActiveRender? active;

    private bool pumping;

    /// <summary>The loopback port the server listens on; 0 before it started.</summary>
    internal int Port { get; private set; }

    /// <summary>The token a connection's first <c>ping</c> must carry: 32 random bytes as hex. Never logged.</summary>
    internal string Token { get; private set; } = "";

    /// <summary>The project the server serves, normalized (<see cref="NormalizeProject"/>).</summary>
    internal string Project { get; } = NormalizeProject(project);

    /// <summary>A project folder as the server compares it: a full path, <c>/</c> separators, no trailing <c>/</c>.</summary>
    /// <param name="path">The folder.</param>
    /// <returns>The normalized path.</returns>
    internal static string NormalizeProject(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    /// <summary>
    /// Listens on <c>127.0.0.1</c> and a free port, then writes the discovery file. Main thread only. A server starts once;
    /// a stopped one is not restarted.
    /// </summary>
    /// <returns>Whether the server is listening; when not, one warning says why.</returns>
    internal bool Start()
    {
        if (started)
        {
            return listener is not null && !stopping;
        }

        started = true;
        if (!Path.IsPathFullyQualified(discoveryFolder))
        {
            GD.PushWarning($"godot-xaml: the preview server did not start: its discovery folder \"{discoveryFolder}\" is not a full path.");
            return false;
        }

        try
        {
            Listen();
            discoveryFile = XscnPreviewDiscovery.Write(discoveryFolder, new PreviewDiscoveryInfo(Port, Token, System.Environment.ProcessId, Project));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SocketException or ArgumentException)
        {
            stopping = true;
            listener?.Dispose();
            listener = null;
            GD.PushWarning($"godot-xaml: the preview server did not start: {exception.Message}");
            return false;
        }

        CancellationToken token = stopSource.Token;
        acceptLoop = Task.Run(() => AcceptAsync(token));
        return true;
    }

    /// <summary>
    /// Runs the render state machine one step: finishes the render in flight once its layout has settled, else starts the
    /// newest pending render unless the editor is scanning or importing. Main thread only; the host calls it each frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
    internal void Pump()
    {
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
        {
            throw new InvalidOperationException("The preview server's pump runs on the main thread only.");
        }

        // A nested main-loop iteration (an import's progress dialog) can call the pump while it instantiates.
        if (pumping)
        {
            return;
        }

        pumping = true;
        try
        {
            Step();
        }
        finally
        {
            pumping = false;
        }
    }

    private void Step()
    {
        if (active is not null)
        {
            if (Engine.GetProcessFrames() >= active.StartFrame + FramesToSettle)
            {
                Finish(active);
            }

            return;
        }

        if (!Held() && TakeNewest() is { } next)
        {
            Begin(next);
        }
    }

    /// <summary>
    /// Stops the server: removes the discovery file, answers every queued render <see cref="XscnPreviewProtocol.Stopping"/>,
    /// frees a half-done render, closes the listener and every connection, and joins its tasks within a bound, warning on
    /// an overrun. Main thread only; synchronous and idempotent.
    /// </summary>
    internal void Stop()
    {
        List<PendingRender> failed;
        Connection[] open;
        lock (gate)
        {
            if (stopping)
            {
                return;
            }

            stopping = true;
            failed = [.. pending.Values];
            pending.Clear();
            open = [.. connections];
        }

        RemoveDiscoveryFile();
        if (active is not null)
        {
            failed.Add(active.Render);
            FreeIfValid(active.Viewport);
            active = null;
        }

        foreach (PendingRender render in failed)
        {
            render.Complete(XscnPreviewProtocol.Error(render.Id, XscnPreviewProtocol.Stopping));
        }

        Flush(open);
        Close(open);
    }

    /// <summary>Stops the server (<see cref="Stop"/>).</summary>
    public void Dispose() => Stop();

    private static bool Held() => Engine.IsEditorHint() && EditorBusy();

#if TOOLS
    // A render waits while the editor scans or imports: the import's progress dialog runs nested iterations.
    private static bool EditorBusy()
    {
        EditorFileSystem fileSystem = EditorInterface.Singleton.GetResourceFilesystem();
        return fileSystem.IsScanning() || fileSystem.IsImporting();
    }
#else
    private static bool EditorBusy() => false;
#endif

    private static bool IsDocumentPath(string document) =>
        document.StartsWith("res://", StringComparison.Ordinal)
        && document.EndsWith(".xscn", StringComparison.Ordinal)
        && !document.Contains("..", StringComparison.Ordinal)
        && !document.Contains('\\', StringComparison.Ordinal)
        && !document.Contains('\0', StringComparison.Ordinal);

    private static bool IsConnectionEnd(Exception exception) =>
        exception
            is IOException
                or InvalidDataException
                or OperationCanceledException
                or ObjectDisposedException
                or SocketException
                or ChannelClosedException;

    private static void FreeIfValid(Node node)
    {
        if (GodotObject.IsInstanceValid(node))
        {
            node.Free();
        }
    }

    private static PreviewRenderResult ResultOf(PreviewScene scene, ImmutableArray<PreviewRect> rects, bool loaded) =>
        new(
            [
                .. rects.Select(rect => new PreviewRectInfo(
                    rect.Path,
                    rect.Class,
                    rect.Kind,
                    rect.Rect.Position.X,
                    rect.Rect.Position.Y,
                    rect.Rect.Size.X,
                    rect.Rect.Size.Y,
                    rect.SpanStart,
                    rect.SpanLength
                )),
            ],
            [
                .. scene.Diagnostics.Select(diagnostic => new PreviewDiagnosticInfo(
                    diagnostic.Code,
                    diagnostic.Severity.ToString(),
                    diagnostic.Message,
                    diagnostic.Span.Start,
                    diagnostic.Span.Length
                )),
            ],
            [.. scene.Issues.Select(issue => new PreviewIssueInfo(issue.Code, issue.Severity.ToString(), issue.Message, issue.Location))],
            loaded
        );

    private static byte[] RenderReply(JsonElement id, PreviewRenderResult result) =>
        XscnPreviewProtocol.Result(id, result, XscnPreviewJsonContext.Default.PreviewRenderResult);

    private static bool WaitBounded(IEnumerable<Task> tasks, TimeSpan bound)
    {
        try
        {
            return Task.WhenAll(tasks).Wait(bound);
        }
        catch (AggregateException)
        {
            return true;
        }
    }

    private void Listen()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        if (OperatingSystem.IsWindows())
        {
            listener.ExclusiveAddressUse = true;
        }

        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Token = System.Security.Cryptography.RandomNumberGenerator.GetHexString(64, lowercase: true);
        tokenBytes = Encoding.UTF8.GetBytes(Token);
    }

    // A failed accept other than the stop's own backs off and goes on; nothing a peer does ends the loop.
    private async Task AcceptAsync(CancellationToken token)
    {
        TimeSpan backoff = FirstBackoff;
        while (!token.IsCancellationRequested)
        {
            TcpClient? client = await AcceptOneAsync(token).ConfigureAwait(false);
            if (client is null)
            {
                backoff = await BackOffAsync(backoff, token).ConfigureAwait(false);
                continue;
            }

            backoff = FirstBackoff;
            AdmitOrClose(client);
        }
    }

    // Waits out the backoff, or until the stop; the next one doubles, up to LastBackoff.
    private static async Task<TimeSpan> BackOffAsync(TimeSpan backoff, CancellationToken token)
    {
        try
        {
            await Task.Delay(backoff, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        return backoff * 2 < LastBackoff ? backoff * 2 : LastBackoff;
    }

    private void AdmitOrClose(TcpClient client)
    {
        try
        {
            Admit(client);
        }
#pragma warning disable CA1031 // An admission that throws closes that socket only; the accept loop goes on.
        catch (Exception)
#pragma warning restore CA1031
        {
            client.Dispose();
        }
    }

    private async Task<TcpClient?> AcceptOneAsync(CancellationToken token)
    {
        try
        {
            return await listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException || IsConnectionEnd(exception))
        {
            return null;
        }
    }

    // A peer that is not loopback, or one over the unauthenticated cap, is closed at once.
    private void Admit(TcpClient client)
    {
        bool loopback = client.Client.RemoteEndPoint is IPEndPoint remote && IPAddress.IsLoopback(remote.Address);
        lock (gate)
        {
            if (loopback && !stopping && connections.Count(connection => !connection.Authenticated) < MaxUnauthenticated)
            {
                var connection = new Connection(client, CancellationTokenSource.CreateLinkedTokenSource(stopSource.Token));
                connections.Add(connection);
                connection.Writing = Task.Run(connection.WriteLoopAsync);
                connection.Serving = Task.Run(() => ServeAsync(connection));
                return;
            }
        }

        client.Dispose();
    }

    private async Task ServeAsync(Connection connection)
    {
        try
        {
            var reader = new XscnPreviewFrameReader(connection.Stream);
            if (await AuthenticateAsync(connection, reader).ConfigureAwait(false))
            {
                await DispatchAllAsync(connection, reader).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (IsConnectionEnd(exception)) { }
        finally
        {
            await LeaveAsync(connection).ConfigureAwait(false);
        }
    }

    private async Task<bool> AuthenticateAsync(Connection connection, XscnPreviewFrameReader reader)
    {
        byte[]? body;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(connection.Token))
        {
            deadline.CancelAfter(AuthenticationDeadline);
            body = await reader.ReadAsync(XscnPreviewFraming.MaxUnauthenticatedBody, deadline.Token).ConfigureAwait(false);
        }

        if (body is null)
        {
            return false;
        }

        byte[] reply = AuthenticationReply(body, out bool admitted);
        // Over the authenticated cap, the connection is closed without a reply.
        if (admitted && !Promote(connection))
        {
            return false;
        }

        await connection.SendAsync(reply).ConfigureAwait(false);
        return admitted;
    }

    private byte[] AuthenticationReply(byte[] body, out bool admitted)
    {
        admitted = false;
        PreviewRequest? request = XscnPreviewProtocol.Parse(body, out int error);
        if (request is null)
        {
            return XscnPreviewProtocol.Error(null, error);
        }

        return request.Id is { } id && request.Method == XscnPreviewProtocol.PingMethod
            ? PingReply(id, request.Params, out admitted)
            : XscnPreviewProtocol.Error(request.Id, XscnPreviewProtocol.NotAuthenticated);
    }

    // The token is checked before the project, so a client without it learns nothing of the editor's project.
    private byte[] PingReply(JsonElement id, JsonElement parameters, out bool admitted)
    {
        admitted = false;
        string? token = XscnPreviewProtocol.StringParam(parameters, "token");
        string? claimed = XscnPreviewProtocol.StringParam(parameters, "project");
        if (token is null || claimed is null)
        {
            return XscnPreviewProtocol.Error(id, XscnPreviewProtocol.InvalidParams);
        }

        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), tokenBytes))
        {
            return XscnPreviewProtocol.Error(id, XscnPreviewProtocol.NotAuthenticated);
        }

        string normalized = NormalizedClaim(claimed);
        if (!string.Equals(normalized, Project, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return XscnPreviewProtocol.Error(
                id,
                XscnPreviewProtocol.OtherProject,
                $"Another project: this editor has {Project} open, and the ping names {normalized}."
            );
        }

        admitted = true;
        return PingResult(id);
    }

    private static string NormalizedClaim(string claimed)
    {
        try
        {
            return NormalizeProject(claimed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return claimed;
        }
    }

    private byte[] PingResult(JsonElement id) =>
        XscnPreviewProtocol.Result(
            id,
            new PreviewPingResult(XscnPreviewProtocol.Version, System.Environment.ProcessId, Project),
            XscnPreviewJsonContext.Default.PreviewPingResult
        );

    private bool Promote(Connection connection)
    {
        lock (gate)
        {
            if (stopping || connections.Count(other => other.Authenticated) >= MaxAuthenticated)
            {
                return false;
            }

            connection.Authenticated = true;
            return true;
        }
    }

    private async Task DispatchAllAsync(Connection connection, XscnPreviewFrameReader reader)
    {
        while (await reader.ReadAsync(XscnPreviewFraming.MaxAuthenticatedBody, connection.Token).ConfigureAwait(false) is { } body)
        {
            if (Dispatch(connection, body) is { } reply)
            {
                await connection.SendAsync(reply).ConfigureAwait(false);
            }
        }
    }

    // The reply to send now, or null for a notification or a queued render, whose reply comes later.
    private byte[]? Dispatch(Connection connection, byte[] body)
    {
        PreviewRequest? request = XscnPreviewProtocol.Parse(body, out int error);
        if (request is null)
        {
            return XscnPreviewProtocol.Error(null, error);
        }

        if (request.Id is not { } id)
        {
            Notify(connection, request);
            return null;
        }

        return request.Method switch
        {
            XscnPreviewProtocol.PingMethod => PingResult(id),
            XscnPreviewProtocol.RenderMethod => Render(connection, id, request.Params),
            _ => XscnPreviewProtocol.Error(id, XscnPreviewProtocol.MethodNotFound),
        };
    }

    private byte[]? Render(Connection connection, JsonElement id, JsonElement parameters)
    {
        PreviewRenderParams? render = XscnPreviewProtocol.RenderParams(parameters);
        if (render is null)
        {
            return XscnPreviewProtocol.Error(id, XscnPreviewProtocol.InvalidParams);
        }

        return IsDocumentPath(render.Document)
            ? Enqueue(new PendingRender(connection, id, render))
            : XscnPreviewProtocol.Error(id, XscnPreviewProtocol.InvalidDocument);
    }

    // Queues the render, superseding the connection's older one for its document; the older one is answered outside the lock.
    private byte[]? Enqueue(PendingRender render)
    {
        PendingRender? superseded;
        lock (gate)
        {
            if (stopping)
            {
                return XscnPreviewProtocol.Error(render.Id, XscnPreviewProtocol.Stopping);
            }

            (Connection, string) key = (render.Connection, render.Parameters.Document);
            if (
                !pending.TryGetValue(key, out superseded)
                && pending.Keys.Count(queued => queued.Connection == render.Connection) >= MaxQueuedPerConnection
            )
            {
                return XscnPreviewProtocol.Error(render.Id, XscnPreviewProtocol.Cancelled);
            }

            render.Sequence = ++sequence;
            pending[key] = render;
        }

        superseded?.Complete(XscnPreviewProtocol.Error(superseded.Id, XscnPreviewProtocol.Cancelled));
        return null;
    }

    // $/cancelRequest answers a queued render of this connection; any other notification is ignored.
    private void Notify(Connection connection, PreviewRequest notification)
    {
        if (notification.Method != XscnPreviewProtocol.CancelMethod || XscnPreviewProtocol.CancelledId(notification.Params) is not { } target)
        {
            return;
        }

        PendingRender? cancelled;
        lock (gate)
        {
            cancelled = pending.Values.FirstOrDefault(render => render.Connection == connection && XscnPreviewProtocol.SameId(render.Id, target));
            if (cancelled is not null)
            {
                pending.Remove((connection, cancelled.Parameters.Document));
            }
        }

        cancelled?.Complete(XscnPreviewProtocol.Error(cancelled.Id, XscnPreviewProtocol.Cancelled));
    }

    // Sends what the outbox holds, then drops the connection and its queued renders; a faulted writer never keeps the slot.
    private async Task LeaveAsync(Connection connection)
    {
        connection.CloseOutbox();
        try
        {
            await connection.Writing.WaitAsync(WriteTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException) { }
        finally
        {
            lock (gate)
            {
                connections.Remove(connection);
                foreach ((Connection, string) key in pending.Keys.Where(key => key.Connection == connection).ToList())
                {
                    pending.Remove(key);
                }
            }

            connection.Dispose();
        }
    }

    private bool IsStopping()
    {
        lock (gate)
        {
            return stopping;
        }
    }

    private PendingRender? TakeNewest()
    {
        lock (gate)
        {
            if (stopping || pending.Count == 0)
            {
                return null;
            }

            KeyValuePair<(Connection, string), PendingRender> newest = pending.MaxBy(entry => entry.Value.Sequence);
            pending.Remove(newest.Key);
            return newest.Value;
        }
    }

    // Instantiates the text under a SubViewport of the clamped size; a preview without a root is answered at once.
    private void Begin(PendingRender render)
    {
        PreviewRenderParams parameters = render.Parameters;
        PreviewScene scene;
        try
        {
            scene = XscnPreviewScene.Instantiate(parameters.Text, parameters.Document, parameters.Scripts);
        }
#pragma warning disable CA1031 // A render that throws answers an internal error; the server keeps serving.
        catch (Exception)
#pragma warning restore CA1031
        {
            render.Complete(XscnPreviewProtocol.Error(render.Id, XscnPreviewProtocol.InternalError));
            return;
        }

        // A stop during the instantiation (a nested iteration can run one) found this render in neither the queue nor flight.
        if (IsStopping())
        {
            scene.Root?.Free();
            render.Complete(XscnPreviewProtocol.Error(render.Id, XscnPreviewProtocol.Stopping));
            return;
        }

        if (scene.Root is null || scene.Document is null)
        {
            scene.Root?.Free();
            render.Complete(RenderReply(render.Id, ResultOf(scene, [], loaded: false)));
            return;
        }

        var viewport = new SubViewport { Size = new Vector2I(Math.Clamp(parameters.Width, 1, MaxSide), Math.Clamp(parameters.Height, 1, MaxSide)) };
        viewport.AddChild(scene.Root);
        active = new ActiveRender(render, viewport, scene, scene.Root, scene.Document, Engine.GetProcessFrames());
        host.AddChild(viewport);
    }

    private void Finish(ActiveRender current)
    {
        active = null;
        try
        {
            ImmutableArray<PreviewRect> rects = XscnPreviewRects.Collect(current.Root, current.Document);
            current.Render.Complete(RenderReply(current.Render.Id, ResultOf(current.Scene, rects, loaded: true)));
        }
#pragma warning disable CA1031 // A collect that throws answers an internal error; the server keeps serving.
        catch (Exception)
#pragma warning restore CA1031
        {
            current.Render.Complete(XscnPreviewProtocol.Error(current.Render.Id, XscnPreviewProtocol.InternalError));
        }
        finally
        {
            FreeIfValid(current.Viewport);
        }
    }

    private void RemoveDiscoveryFile()
    {
        if (discoveryFile is null)
        {
            return;
        }

        string path = discoveryFile;
        discoveryFile = null;
        if (XscnPreviewDiscovery.Delete(path) is { } failure)
        {
            GD.PushWarning($"godot-xaml: the preview server could not remove its discovery file {path}: {failure}");
        }
    }

    // The replies already handed to the outboxes are sent before the sockets close, within a bound.
    private static void Flush(Connection[] open)
    {
        foreach (Connection connection in open)
        {
            connection.CloseOutbox();
        }

        _ = WaitBounded(open.Select(connection => connection.Writing), FlushBound);
    }

    private void Close(Connection[] open)
    {
        try
        {
            stopSource.Cancel();
        }
        catch (AggregateException) { }

        listener?.Stop();
        listener?.Dispose();
        foreach (Connection connection in open)
        {
            // A cancellation callback that throws never keeps the stop from closing the rest.
            try
            {
                connection.Abort();
            }
            catch (AggregateException) { }
        }

        if (WaitBounded([acceptLoop, .. open.Select(connection => connection.Serving)], JoinBound))
        {
            stopSource.Dispose();
            return;
        }

        GD.PushWarning($"godot-xaml: the preview server's connections did not end within {JoinBound.TotalSeconds} seconds of its stop.");
    }

    // The render in flight: its nodes, freed on every path, and the frame it began on.
    private sealed record ActiveRender(
        PendingRender Render,
        SubViewport Viewport,
        PreviewScene Scene,
        Node Root,
        XscnDocument Document,
        ulong StartFrame
    );

    // A queued render; its reply goes to its connection's outbox exactly once.
    private sealed class PendingRender(Connection connection, JsonElement id, PreviewRenderParams parameters)
    {
        private int completed;

        internal Connection Connection => connection;

        internal JsonElement Id => id;

        internal PreviewRenderParams Parameters => parameters;

        internal long Sequence { get; set; }

        internal void Complete(byte[] reply)
        {
            if (Interlocked.Exchange(ref completed, 1) == 0)
            {
                connection.Post(reply);
            }
        }
    }

    // One client connection: its socket, its cancellation (linked to the server's) and its outbox, which one writer drains.
    private sealed class Connection(TcpClient client, CancellationTokenSource source) : IDisposable
    {
        private const int OutboxCapacity = 64;

        private readonly object sync = new();

        private bool disposed;

        private readonly Channel<byte[]> outbox = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(OutboxCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait }
        );

        internal CancellationToken Token { get; } = source.Token;

        internal NetworkStream Stream { get; } = client.GetStream();

        // Read and written under the server's lock.
        internal bool Authenticated { get; set; }

        internal Task Writing { get; set; } = Task.CompletedTask;

        internal Task Serving { get; set; } = Task.CompletedTask;

        // The reader waits for room in the outbox.
        internal async Task SendAsync(byte[] reply) => await outbox.Writer.WriteAsync(reply, Token).ConfigureAwait(false);

        // The main thread never waits: a full or closed outbox closes the connection instead.
        internal void Post(byte[] reply)
        {
            if (!outbox.Writer.TryWrite(reply))
            {
                Abort();
            }
        }

        internal void CloseOutbox() => outbox.Writer.TryComplete();

        internal async Task WriteLoopAsync()
        {
            try
            {
                while (await outbox.Reader.WaitToReadAsync(Token).ConfigureAwait(false))
                {
                    while (outbox.Reader.TryRead(out byte[]? reply))
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
                        timeout.CancelAfter(WriteTimeout);
                        await XscnPreviewFraming.WriteAsync(Stream, reply, timeout.Token).ConfigureAwait(false);
                    }
                }

                client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception exception) when (IsConnectionEnd(exception))
            {
                Abort();
            }
        }

        // Does nothing once the connection is disposed, so it never cancels a disposed source.
        internal void Abort()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                try
                {
                    source.Cancel();
                }
                catch (AggregateException) { }

                client.Dispose();
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                client.Dispose();
                source.Dispose();
            }
        }
    }
}
