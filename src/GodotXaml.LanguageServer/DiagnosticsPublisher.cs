using System.Collections.Frozen;
using System.Collections.Immutable;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer;

/// <summary>
/// Parses each open document and publishes its diagnostics: at once on open, after a debounce on change. A parse whose
/// buffer changed, closed or reopened meanwhile publishes nothing, and a close's empty list is the last publish for it.
/// </summary>
internal sealed class DiagnosticsPublisher(
    DocumentStore store,
    ProjectCache projects,
    IClientMessages client,
    IDebounceScheduler scheduler,
    ParseCache cache
) : IDisposable
{
    /// <summary>How long a change waits for the next one before it is parsed.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly Lock gate = new();
    private readonly Dictionary<string, CancellationTokenSource> pending = new(StringComparer.Ordinal);
    private readonly HashSet<Task> running = [];
    private readonly SemaphoreSlim publishing = new(1, 1);
    private bool stopped;

    /// <summary>A document opened: parsed and published at once.</summary>
    /// <param name="uri">Its URI.</param>
    /// <param name="version">Its version.</param>
    /// <param name="text">Its text.</param>
    public void Opened(string uri, int version, string text) => Update(() => store.Open(uri, version, text), debounce: false);

    /// <summary>A document changed: parsed once no newer change comes within the debounce.</summary>
    /// <param name="uri">Its URI.</param>
    /// <param name="version">Its new version.</param>
    /// <param name="text">Its whole new text.</param>
    public void Changed(string uri, int version, string text) => Update(() => store.Change(uri, version, text), debounce: true);

    /// <summary>A document closed: its pending parse cancelled, its buffer dropped, and its diagnostics cleared.</summary>
    /// <param name="uri">Its URI.</param>
    public void Closed(string uri)
    {
        CancellationTokenSource? old;
        bool wasOpen;
        lock (gate)
        {
            if (stopped)
            {
                return;
            }
            pending.Remove(uri, out old);
            wasOpen = store.Close(uri);
            cache.Remove(uri);
        }

        old?.Cancel();
        if (wasOpen)
        {
            Task publish = PublishAsync(new PublishDiagnosticsParams(uri, null, []), current: null);
            lock (gate)
            {
                Track(publish);
            }
        }
    }

    /// <summary>Stops scheduling, cancels every pending parse, and waits for the running ones.</summary>
    /// <returns>The wait.</returns>
    public async Task StopAsync()
    {
        CancellationTokenSource[] cancelling;
        Task[] waiting;
        lock (gate)
        {
            stopped = true;
            cancelling = [.. pending.Values];
            pending.Clear();
            waiting = [.. running];
        }

        foreach (CancellationTokenSource source in cancelling)
        {
            source.Cancel();
        }
        await Task.WhenAll(waiting);
    }

    /// <inheritdoc/>
    public void Dispose() => publishing.Dispose();

    /// <summary>A parse's diagnostics as LSP's: 0-based UTF-16 ranges, the code and its link, the server as the source.</summary>
    /// <param name="text">The parsed text.</param>
    /// <param name="diagnostics">The parse's diagnostics.</param>
    /// <returns>The LSP diagnostics.</returns>
    public static LspDiagnostic[] Map(string text, ImmutableArray<XscnDiagnostic> diagnostics)
    {
        LineMap lines = new(text);
        return
        [
            .. diagnostics.Select(d => new LspDiagnostic(
                new LspRange(PositionOf(lines, d.Span.Start, text.Length), PositionOf(lines, d.Span.End, text.Length)),
                d.Severity == XscnSeverity.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                d.Code,
                new CodeDescription(DiagnosticLinks.Href(d.Code)),
                LanguageServerHost.ServerName,
                d.Message
            )),
        ];
    }

    private static LspPosition PositionOf(LineMap lines, int offset, int length)
    {
        LinePosition position = lines.ZeroBased(Math.Clamp(offset, 0, length));
        return new LspPosition(position.Line, position.Column);
    }

    // Applies an open or a change to the store and schedules its run, cancelling the one it replaces. The cancel runs
    // outside the gate: a cancelled run's catch and finally may execute inline inside Cancel(), and they take the gate.
    private void Update(Func<OpenDocument?> apply, bool debounce)
    {
        OpenDocument? document;
        CancellationTokenSource? old;
        lock (gate)
        {
            if (stopped || apply() is not { } updated)
            {
                return;
            }
            document = updated;
            pending.Remove(document.Uri, out old);
        }

        old?.Cancel();
        lock (gate)
        {
            if (stopped)
            {
                return;
            }

            // The run starts under the gate, so its debounce is asked for in the order the changes came.
            CancellationTokenSource source = new();
            pending[document.Uri] = source;
            Track(RunAsync(document, debounce, source));
        }
    }

    // Called under the gate.
    private void Track(Task task)
    {
        running.Add(task);
        _ = task.ContinueWith(
            done =>
            {
                lock (gate)
                {
                    running.Remove(done);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private async Task RunAsync(OpenDocument document, bool debounce, CancellationTokenSource source)
    {
        CancellationToken cancellation = source.Token;
        try
        {
            if (debounce)
            {
                await scheduler.Delay(Debounce, cancellation).ConfigureAwait(false);
            }
            else
            {
                await Task.Yield();
            }

            ProjectContext? project = await projects.ResolveAsync(document).ConfigureAwait(false);
            if (project is null)
            {
                return;
            }

            XscnParseResult result = await Task.Run(() => cache.Get(document, project.Metadata), cancellation).ConfigureAwait(false);
            await PublishAsync(new PublishDiagnosticsParams(document.Uri, document.Version, Map(document.Text, result.Diagnostics)), document)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer change, a close or the shutdown took over.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A failure, a cancellation the document's token did not raise included, is a failed check, not a fault.
            await client.LogAsync(MessageType.Error, $"Checking {document.Uri} failed: {exception}").ConfigureAwait(false);
        }
        finally
        {
            // The source is not disposed: one without a timer holds nothing to free, and a Cancel() that races the
            // run's end stays safe.
            lock (gate)
            {
                if (pending.TryGetValue(document.Uri, out CancellationTokenSource? current) && current == source)
                {
                    pending.Remove(document.Uri);
                }
            }
        }
    }

    // One publish at a time, the buffer checked inside, so an older parse never lands after a newer one or a close.
    private async Task PublishAsync(PublishDiagnosticsParams parameters, OpenDocument? current)
    {
        await publishing.WaitAsync().ConfigureAwait(false);
        try
        {
            if (current is null || store.IsCurrent(current))
            {
                await client.PublishAsync(parameters).ConfigureAwait(false);
            }
        }
        finally
        {
            publishing.Release();
        }
    }
}

/// <summary>Where each diagnostic code is described: its heading in <c>docs/format/diagnostics.md</c>.</summary>
internal static class DiagnosticLinks
{
    /// <summary>The diagnostics reference.</summary>
    public const string Page = "https://github.com/leftos/godot-xaml/blob/main/docs/format/diagnostics.md";

    // The anchor GitHub gives each code's heading; a test reads the headings and checks every one.
    private static readonly FrozenDictionary<string, string> Anchors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [XscnCodes.XmlSyntax] = "gx0001-not-well-formed-xml",
        [XscnCodes.CommentInStartTag] = "gx0002-comment-inside-a-start-tag",
        [XscnCodes.UnexpectedText] = "gx0003-unexpected-text",
        [XscnCodes.MissingRoot] = "gx0004-no-root-element",
        [XscnCodes.UnknownFormatElement] = "gx0101-unknown-x-element",
        [XscnCodes.UnknownFormatAttribute] = "gx0102-unknown-x-attribute",
        [XscnCodes.UndeclaredPrefix] = "gx0103-undeclared-prefix",
        [XscnCodes.MissingName] = "gx0104-node-without-a-name",
        [XscnCodes.InvalidValue] = "gx0105-invalid-value",
        [XscnCodes.UnknownEnumMember] = "gx0106-unknown-enum-member",
        [XscnCodes.UnknownClass] = "gx0107-unknown-class",
        [XscnCodes.MissingAttribute] = "gx0108-missing-attribute",
        [XscnCodes.MisplacedNamespace] = "gx0109-misplaced-namespace-declaration",
        [XscnCodes.InvalidNamespace] = "gx0110-invalid-namespace",
        [XscnCodes.MisplacedElement] = "gx0111-misplaced-element",
        [XscnCodes.InvalidPropertyElement] = "gx0112-invalid-property-element",
        [XscnCodes.DuplicateAttribute] = "gx0113-repeated-attribute",
        [XscnCodes.ScenePrefixedAttribute] = "gx0114-scene-prefix-on-an-attribute",
        [XscnCodes.SecondTable] = "gx0115-second-table",
        [XscnCodes.RepeatedKey] = "gx0116-repeated-key",
        [XscnCodes.NotAResource] = "gx0117-not-a-resource",
        [XscnCodes.SignalAndGodotSignal] = "gx0118-signal-and-godotsignal",
        [XscnCodes.PlaceholderRoot] = "gx0119-placeholder-at-the-root",
        [XscnCodes.NestingTooDeep] = "gx0120-nested-too-deep",
        [XscnCodes.MalformedGodotMarker] = "gx0121-malformed-xgodot",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The link to a code's heading, or to the reference itself for a code it does not know.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The link.</returns>
    public static string Href(string code) => Anchors.TryGetValue(code, out string? anchor) ? $"{Page}#{anchor}" : Page;
}
